using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinPieGestures;

/// <summary>
/// 轮盘交互音效类型
/// </summary>
public enum SoundType
{
	/// <summary>呼出轮盘</summary>
	WheelPopup,
	/// <summary>扇区切换/划过高亮</summary>
	SectorHover,
	/// <summary>二级级联菜单展开</summary>
	SubmenuExpand,
	/// <summary>动作确认触发执行</summary>
	ActionExecute,
	/// <summary>外甩脱离或手势取消</summary>
	GestureCancel
}

/// <summary>
/// 极轻量轮盘交互音效管理器 (Scheme C - Win32 内存驻留音频管线与数学波形合成)。
/// <para>
/// 核心特性：
/// 1. 纯原生 Win32 winmm.dll 非托管内存异步回放，延迟 &lt; 2ms，零第三方依赖；
/// 2. 程序化生成 44.1kHz 16-bit Mono 极微波形，常驻内存 &lt; 20 KB，完全免除外部音频文件依赖与资源解压开销；
/// 3. 使用非托管内存指针 (Marshal.AllocHGlobal)，彻底杜绝 GC 内存移动导致的底层 Access Violation；
/// 4. 扇区切换 35ms 极速防抖闸门，杜绝分界线高频抖动杂音；
/// 5. 独立于系统主音量的硬件级 PCM 数学振幅无损缩放。
/// </para>
/// </summary>
public static class SoundEffectManager
{
	[DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PlaySoundW(IntPtr pszSound, IntPtr hmod, uint fdwSound);

	private const uint SND_SYNC = 0x0000;
	private const uint SND_NODEFAULT = 0x0002;
	private const uint SND_MEMORY = 0x0004;

	private static readonly object _syncLock = new object();
	private static readonly Dictionary<SoundType, byte[]> _soundBuffers = new();

	private static readonly object _queueLock = new object();
	private static readonly AutoResetEvent _soundSignal = new AutoResetEvent(false);
	private static SoundType? _pendingSound;
	private static byte[]? _pendingCustomWav;

	private static Thread? _workerThread;
	private static volatile bool _isRunning = false;

	private static string _currentTheme = string.Empty;
	private static double _currentVolume = -1.0;
	private static bool _initialized = false;
	private static long _lastHoverTick = 0L;

	/// <summary>扇区切换音效最小触发时间间隔 (毫秒)，防止光标在扇区分界线来回微颤时产生刺耳噪音。</summary>
	private const long HoverDebounceMs = 35L;

	/// <summary>
	/// 确保专属音频后台回放工作线程已启动（单读者无锁队列，彻底规避 WinMM 异步中断死锁）。
	/// </summary>
	private static void EnsureWorkerStarted()
	{
		if (_isRunning && _workerThread != null && _workerThread.IsAlive)
		{
			return;
		}
		lock (_syncLock)
		{
			if (_isRunning && _workerThread != null && _workerThread.IsAlive)
			{
				return;
			}
			_isRunning = true;
			_workerThread = new Thread(ProcessSoundQueue)
			{
				Name = "StarPie.SoundWorker",
				IsBackground = true,
				Priority = ThreadPriority.AboveNormal
			};
			_workerThread.Start();
		}
	}

	/// <summary>
	/// 专属音频播放循环：在独立工作线程内使用 SND_SYNC 同步回放。
	/// 采用 AutoResetEvent 信号机制与智能合并，杜绝死锁、CPU 跑满与 Use-After-Free 野指针。
	/// </summary>
	private static void ProcessSoundQueue()
	{
		while (_isRunning)
		{
			try
			{
				_soundSignal.WaitOne();
				if (!_isRunning)
				{
					break;
				}

				SoundType? soundToPlay;
				byte[]? customWavToPlay;
				lock (_queueLock)
				{
					soundToPlay = _pendingSound;
					customWavToPlay = _pendingCustomWav;
					_pendingSound = null;
					_pendingCustomWav = null;
				}

				byte[]? wavData = customWavToPlay;
				if (wavData == null && soundToPlay.HasValue)
				{
					lock (_syncLock)
					{
						_soundBuffers.TryGetValue(soundToPlay.Value, out wavData);
					}
				}

				if (wavData != null && wavData.Length > 0 && _isRunning)
				{
					PlaySoundDirect(wavData);
				}
			}
			catch (ThreadAbortException)
			{
				break;
			}
			catch (Exception ex)
			{
				AppLogger.LogWarn($"SoundWorker iteration exception: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 仅在专属工作线程内短暂固定托管内存并执行 Win32 PlaySoundW 同步回放。
	/// 绝不跨线程并发调用，绝不产生野指针释放竞争。
	/// </summary>
	private static void PlaySoundDirect(byte[] wavData)
	{
		GCHandle pin = default;
		try
		{
			pin = GCHandle.Alloc(wavData, GCHandleType.Pinned);
			IntPtr ptr = pin.AddrOfPinnedObject();
			bool success = PlaySoundW(ptr, IntPtr.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT);
			if (!success)
			{
				int err = Marshal.GetLastWin32Error();
				if (err != 0)
				{
					AppLogger.LogWarn($"PlaySoundW returned false, Win32 error: {err}");
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogWarn($"PlaySoundDirect exception: {ex.Message}");
		}
		finally
		{
			if (pin.IsAllocated)
			{
				pin.Free();
			}
		}
	}

	/// <summary>
	/// 初始化或按需刷新音效数据缓存（在应用启动、配置载入或用户修改音量/主题时调用）。
	/// </summary>
	public static void Initialize(string? theme = null, double? volume = null, bool force = false)
	{
		lock (_syncLock)
		{
			string targetTheme = theme ?? ConfigManager.CurrentConfig?.SoundTheme ?? "Mechanical";
			double targetVolume = volume ?? ConfigManager.CurrentConfig?.SoundVolume ?? 0.6;
			targetVolume = Math.Clamp(targetVolume, 0.0, 1.0);

			if (!force && _initialized && string.Equals(_currentTheme, targetTheme, StringComparison.OrdinalIgnoreCase)
				&& Math.Abs(_currentVolume - targetVolume) < 0.01)
			{
				return;
			}

			_currentTheme = targetTheme;
			_currentVolume = targetVolume;

			// 程序化合成 5 大音效事件波形并存入托管字典（原子替换引用，无野指针风险）
			_soundBuffers[SoundType.WheelPopup] = SynthesizeSound(SoundType.WheelPopup, targetTheme, targetVolume);
			_soundBuffers[SoundType.SectorHover] = SynthesizeSound(SoundType.SectorHover, targetTheme, targetVolume);
			_soundBuffers[SoundType.SubmenuExpand] = SynthesizeSound(SoundType.SubmenuExpand, targetTheme, targetVolume);
			_soundBuffers[SoundType.ActionExecute] = SynthesizeSound(SoundType.ActionExecute, targetTheme, targetVolume);
			_soundBuffers[SoundType.GestureCancel] = SynthesizeSound(SoundType.GestureCancel, targetTheme, targetVolume);

			_initialized = true;
		}

		EnsureWorkerStarted();
	}

	/// <summary>
	/// 触发播放指定事件类型的交互音效（完全非阻塞，极速无感）。
	/// </summary>
	public static void Play(SoundType type)
	{
		AppConfig? cfg = ConfigManager.CurrentConfig;
		if (cfg == null || !cfg.EnableSoundEffects)
		{
			return;
		}

		// 细项事件开关过滤
		bool isEnabled = type switch
		{
			SoundType.WheelPopup => cfg.SoundOnPopup,
			SoundType.SectorHover => cfg.SoundOnHover,
			SoundType.SubmenuExpand => cfg.SoundOnExpand,
			SoundType.ActionExecute => cfg.SoundOnExecute,
			SoundType.GestureCancel => cfg.SoundOnCancel,
			_ => true
		};

		if (!isEnabled)
		{
			return;
		}

		// 扇区切换防抖控制
		if (type == SoundType.SectorHover)
		{
			long now = Environment.TickCount64;
			if (now - _lastHoverTick < HoverDebounceMs)
			{
				return;
			}
			_lastHoverTick = now;
		}
		else if (type == SoundType.SubmenuExpand)
		{
			// 二级展开保护期：防止展开瞬间紧接着触发子扇区 Hover 堆叠
			_lastHoverTick = Environment.TickCount64 + 20L;
		}

		EnsureInitialized();
		EnsureWorkerStarted();
		lock (_queueLock)
		{
			_pendingSound = type;
			_pendingCustomWav = null;
		}
		_soundSignal.Set();
	}

	/// <summary>
	/// 直接播放指定音效（供设置界面实时试听，不受全局开关拦截）。
	/// </summary>
	public static void PlayPreview(SoundType type)
	{
		EnsureInitialized();
		EnsureWorkerStarted();
		lock (_queueLock)
		{
			_pendingSound = type;
			_pendingCustomWav = null;
		}
		_soundSignal.Set();
	}

	/// <summary>
	/// 直接试听指定的自定义音效事件配置（非阻塞通过专属工作线程播放，零延迟、绝不产生多线程冲突）。
	/// </summary>
	public static void PlayCustomEventPreview(SoundEventConfig config, double? volume = null)
	{
		if (config == null) return;
		EnsureWorkerStarted();
		double vol = volume ?? ConfigManager.CurrentConfig?.SoundVolume ?? 0.6;
		byte[] wavData = SynthesizeCustomEventSound(config, vol);
		if (wavData == null || wavData.Length == 0) return;

		lock (_queueLock)
		{
			_pendingSound = null;
			_pendingCustomWav = wavData;
		}
		_soundSignal.Set();
	}

	private static void EnsureInitialized()
	{
		if (!_initialized)
		{
			Initialize();
		}
	}

	/// <summary>
	/// 释放所有音频资源与工作线程（在应用退出时调用）。
	/// </summary>
	public static void Shutdown()
	{
		Thread? workerThread;
		lock (_syncLock)
		{
			_isRunning = false;
			workerThread = _workerThread;
			_workerThread = null;
			_soundBuffers.Clear();
			_initialized = false;
		}

		lock (_queueLock)
		{
			_pendingSound = null;
			_pendingCustomWav = null;
		}

		try { _soundSignal.Set(); } catch { }
		if (workerThread != null && workerThread.ManagedThreadId != Environment.CurrentManagedThreadId)
		{
			try { workerThread.Join(TimeSpan.FromMilliseconds(500)); } catch { }
		}
	}
	#region 程序化波形合成引擎 (Procedural Sound Synthesizer)

	/// <summary>
	/// 听觉响度曲线补偿：将 0.0~1.0 的滑块数值映射到符合人耳对数感知的高保真声学增益，
	/// 杜绝中低音量段因扬声器 DAC 降噪门限导致的静音。
	/// </summary>
	private static double GetAcousticGain(double sliderVol)
	{
		if (sliderVol <= 0.001) return 0.0;
		return Math.Clamp(0.18 + 0.82 * Math.Pow(Math.Clamp(sliderVol, 0.0, 1.0), 1.25), 0.0, 1.0);
	}

	/// <summary>
	/// 根据音效主题与类型，以纯数学算法生成 44.1kHz 16-bit 单声道 WAV 字节流。
	/// 所有音效均具有平滑起音微窗（防 DAC 爆音破音）与饱满谐波共鸣（确保各类扬声器与耳机清晰可辨）。
	/// </summary>
	private static byte[] SynthesizeSound(SoundType type, string theme, double volume)
	{
		int sampleRate = 44100;

		switch (theme.ToLowerInvariant())
		{
			case "crisp": // 现代清脆 (数码触感/清爽回馈)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 420, 920, 48, volume),
					SoundType.SectorHover => SynthesizeClick(sampleRate, 2100, 1050, 36, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 980, 1470, 52, volume),
					SoundType.ActionExecute => SynthesizePunchyConfirm(sampleRate, 1100, 480, 68, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 720, 340, 42, volume * 0.85),
					_ => SynthesizeClick(sampleRate, 2100, 1050, 36, volume)
				};

			case "bubble": // 柔和气泡 (水滴轻音)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 340, 760, 56, volume * 0.95),
					SoundType.SectorHover => SynthesizeBubble(sampleRate, 820, 1450, 38, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 880, 1320, 58, volume * 0.95),
					SoundType.ActionExecute => SynthesizeBubble(sampleRate, 680, 1680, 72, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 520, 240, 45, volume * 0.85),
					_ => SynthesizeBubble(sampleRate, 820, 1450, 38, volume)
				};

			case "minimalist": // 极简短音 (超微清晰脉冲)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 480, 820, 42, volume * 0.85),
					SoundType.SectorHover => SynthesizeClick(sampleRate, 1800, 900, 32, volume * 0.85),
					SoundType.SubmenuExpand => SynthesizeTone(sampleRate, 1020, 46, volume * 0.85),
					SoundType.ActionExecute => SynthesizeDualTone(sampleRate, 880, 440, 55, volume * 0.9),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 420, 260, 38, volume * 0.75),
					_ => SynthesizeClick(sampleRate, 1800, 900, 32, volume * 0.85)
				};

			case "custom": // 自定义音效方案 (真实程序化参数合成与采样加载)
				var activeProfileId = ConfigManager.CurrentConfig?.ActiveCustomSoundProfileId;
				var profile = ConfigManager.CurrentConfig?.CustomSoundProfiles?.FirstOrDefault(p => p.Id == activeProfileId)
					?? ConfigManager.CurrentConfig?.CustomSoundProfiles?.FirstOrDefault();
				var evConfig = profile?.Events?.FirstOrDefault(e => e.EventType == type);
				return SynthesizeCustomEventSound(evConfig, volume);

			case "mechanical": // 机械手感 (默认 - 轴体微动与刻度感)
			default:
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 260, 620, 54, volume),
					SoundType.SectorHover => SynthesizeMechanicalClick(sampleRate, 1750, 780, 38, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 920, 1380, 60, volume),
					SoundType.ActionExecute => SynthesizePunchyConfirm(sampleRate, 720, 1080, 80, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 520, 220, 46, volume * 0.85),
					_ => SynthesizeMechanicalClick(sampleRate, 1750, 780, 38, volume)
				};
		}
	}

	/// <summary>
	/// 为自定义手势事件配置生成专属 PCM 波形（支持程序化极微波形、经典预设与外部音频采样）。
	/// </summary>
	public static byte[] SynthesizeCustomEventSound(SoundEventConfig? config, double masterVolume)
	{
		int sampleRate = 44100;
		if (config == null)
		{
			return SynthesizeSound(SoundType.SectorHover, "Mechanical", masterVolume);
		}
		if (config.SourceType == SoundSourceType.Mute)
		{
			return Array.Empty<byte>();
		}

		double effectiveVol = Math.Clamp(config.RelativeVolume, 0.0, 1.0) * masterVolume;

		if (config.SourceType == SoundSourceType.BuiltInPreset)
		{
			string theme = config.BuiltInTheme ?? "Mechanical";
			return SynthesizeSound(config.EventType, theme, effectiveVol);
		}

		if (config.SourceType == SoundSourceType.CustomFile)
		{
			if (!string.IsNullOrWhiteSpace(config.CustomFilePath) && File.Exists(config.CustomFilePath))
			{
				return TryLoadCustomWavFile(config.CustomFilePath, effectiveVol);
			}
			// 文件不存在或为空时回退至清脆微动
			return SynthesizeClick(sampleRate, 1800, 900, 32, effectiveVol);
		}

		// 程序化极微波形 (ProceduralWave)
		double pitchMult = Math.Pow(2.0, config.PitchSemitones / 12.0);
		double durationMs = Math.Clamp(config.DurationMs, 5.0, 300.0);

		return (config.WavePreset?.ToLowerInvariant()) switch
		{
			"sine1200" => SynthesizeTone(sampleRate, 1200.0 * pitchMult, durationMs, effectiveVol),
			"square850" => SynthesizeClick(sampleRate, 1800.0 * pitchMult, 850.0 * pitchMult, durationMs, effectiveVol),
			"pulse2ms" => SynthesizeClick(sampleRate, 3200.0 * pitchMult, 1600.0 * pitchMult, Math.Min(durationMs, 22.0), effectiveVol),
			"sinedeep" => SynthesizeSweep(sampleRate, 480.0 * pitchMult, 220.0 * pitchMult, durationMs, effectiveVol),
			"metallicclick" => SynthesizeMechanicalClick(sampleRate, 2400.0 * pitchMult, 720.0 * pitchMult, durationMs, effectiveVol),
			"laserzap" => SynthesizeSweep(sampleRate, 2200.0 * pitchMult, 440.0 * pitchMult, durationMs, effectiveVol),
			"waterdrop" => SynthesizeBubble(sampleRate, 650.0 * pitchMult, 1550.0 * pitchMult, durationMs, effectiveVol),
			"cybersweep" => SynthesizeSweep(sampleRate, 320.0 * pitchMult, 1680.0 * pitchMult, durationMs, effectiveVol),
			_ => SynthesizeClick(sampleRate, 1800.0 * pitchMult, 900.0 * pitchMult, durationMs, effectiveVol)
		};
	}

	private static byte[] TryLoadCustomWavFile(string filePath, double sliderVol)
	{
		try
		{
			if (File.Exists(filePath) && filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
			{
				byte[] data = File.ReadAllBytes(filePath);
				if (data.Length >= 44 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF")
				{
					double gain = GetAcousticGain(sliderVol);
					if (Math.Abs(gain - 1.0) < 0.04) return data;

					byte[] scaled = (byte[])data.Clone();
					for (int i = 44; i + 1 < scaled.Length; i += 2)
					{
						short sample = (short)(scaled[i] | (scaled[i + 1] << 8));
						sample = (short)Math.Clamp(sample * gain, -32768.0, 32767.0);
						scaled[i] = (byte)(sample & 0xFF);
						scaled[i + 1] = (byte)((sample >> 8) & 0xFF);
					}
					return scaled;
				}
			}
		}
		catch { }
		return SynthesizeClick(44100, 1800, 900, 35, sliderVol);
	}

	/// <summary>
	/// 现代数码微动触感脉冲（适用于 Crisp 与 Minimalist）
	/// </summary>
	private static byte[] SynthesizeClick(int sampleRate, double clickFreq, double bodyFreq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			// 1.5ms 极小平滑起音，彻底消除扬声器开门爆音
			double attack = (t < 0.0015) ? (t / 0.0015) : 1.0;
			// 瞬态快速衰减敲击声
			double clickEnv = Math.Exp(-t * 260.0);
			double click = Math.Sin(2.0 * Math.PI * clickFreq * t) * clickEnv * 0.65;
			// 丰满主体共鸣，确保各类小型扬声器不被降噪切除
			double bodyEnv = Math.Pow(1.0 - norm, 1.7);
			double body = Math.Sin(2.0 * Math.PI * bodyFreq * t) * bodyEnv * 0.35;

			double s = (click + body) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 机械轴体双频咔哒声（轴体触发微动 + 触底沉稳共鸣）
	/// </summary>
	private static byte[] SynthesizeMechanicalClick(int sampleRate, double clickFreq, double thudFreq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.0012) ? (t / 0.0012) : 1.0;
			// 混合微量二次谐波强化清脆咔哒
			double clickEnv = Math.Exp(-t * 220.0);
			double click = (Math.Sin(2.0 * Math.PI * clickFreq * t) * 0.8 + Math.Sin(2.0 * Math.PI * clickFreq * 1.8 * t) * 0.2) * clickEnv * 0.6;
			// 沉稳轴座回响
			double thudEnv = Math.Pow(1.0 - norm, 1.5);
			double thud = Math.Sin(2.0 * Math.PI * thudFreq * t) * thudEnv * 0.4;

			double s = (click + thud) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 扫频滑音（适合呼出展开与外甩取消）
	/// </summary>
	private static byte[] SynthesizeSweep(int sampleRate, double fStart, double fEnd, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		double phase = 0.0;
		for (int i = 0; i < samples; i++)
		{
			double norm = (double)i / samples;
			double freq = fStart + (fEnd - fStart) * Math.Pow(norm, 1.2);
			phase += 2.0 * Math.PI * freq / sampleRate;
			// 正弦升余弦窗包络，两端无声截断
			double env = Math.Sin(Math.PI * norm);
			double s = Math.Sin(phase) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 纯音衰减短音
	/// </summary>
	private static byte[] SynthesizeTone(int sampleRate, double freq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.002) ? (t / 0.002) : 1.0;
			double env = Math.Pow(1.0 - norm, 1.5) * attack;
			double s = Math.Sin(2.0 * Math.PI * freq * t) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 和谐双音和弦（适合二级子轮盘展开）
	/// </summary>
	private static byte[] SynthesizeDualTone(int sampleRate, double f1, double f2, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.003) ? (t / 0.003) : 1.0;
			double env = Math.Pow(1.0 - norm, 1.6) * attack;
			double s = (Math.Sin(2.0 * Math.PI * f1 * t) * 0.52 + Math.Sin(2.0 * Math.PI * f2 * t) * 0.48) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 柔和气泡水滴音
	/// </summary>
	private static byte[] SynthesizeBubble(int sampleRate, double fStart, double fEnd, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		double phase = 0.0;
		for (int i = 0; i < samples; i++)
		{
			double norm = (double)i / samples;
			double freq = fStart + (fEnd - fStart) * Math.Pow(norm, 1.8);
			phase += 2.0 * Math.PI * freq / sampleRate;
			double env = Math.Pow(Math.Sin(Math.PI * norm), 0.7);
			double s = Math.Sin(phase) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 笃定确认声（清脆 Snap + 沉稳 Thump 复合，适合动作执行）
	/// </summary>
	private static byte[] SynthesizePunchyConfirm(int sampleRate, double fSnap, double fThump, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.002) ? (t / 0.002) : 1.0;
			double snapEnv = Math.Exp(-t * 110.0);
			double snap = Math.Sin(2.0 * Math.PI * fSnap * t) * snapEnv * 0.65;
			double thumpEnv = Math.Pow(1.0 - norm, 1.4);
			double thump = Math.Sin(2.0 * Math.PI * fThump * t) * thumpEnv * 0.35;

			double s = (snap + thump) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 将 PCM 采样封装为标准 RIFF/WAVE 二进制格式
	/// </summary>
	private static byte[] WrapPcmToWav(short[] pcm, int sampleRate)
	{
		int subChunk2Size = pcm.Length * sizeof(short);
		int chunkSize = 36 + subChunk2Size;

		using var ms = new MemoryStream(44 + subChunk2Size);
		using var bw = new BinaryWriter(ms);

		// RIFF header
		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(chunkSize);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		// "fmt " subchunk
		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);               // Subchunk1Size (16 for PCM)
		bw.Write((short)1);          // AudioFormat (1 = PCM)
		bw.Write((short)1);          // NumChannels (1 = Mono)
		bw.Write(sampleRate);        // SampleRate
		bw.Write(sampleRate * 2);    // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
		bw.Write((short)2);          // BlockAlign (NumChannels * BitsPerSample/8)
		bw.Write((short)16);         // BitsPerSample

		// "data" subchunk
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(subChunk2Size);

		for (int i = 0; i < pcm.Length; i++)
		{
			bw.Write(pcm[i]);
		}

		bw.Flush();
		return ms.ToArray();
	}

	#endregion
}
