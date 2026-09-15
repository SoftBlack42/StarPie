using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace WinPieGestures;

/// <summary>
/// 平铺窗口执行器：把当前虚拟桌面上的可见窗口按预置布局平铺到各自主显示器工作区。
/// 纯物理像素 SetWindowPos；不激活、不抢焦点；由 ActionExecutor 的后台队列调用。
/// </summary>
public static class WindowTiler
{
	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MONITORINFO
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int x;
		public int y;
	}

	private const uint MONITOR_DEFAULTTONEAREST = 2;

	[DllImport("user32.dll")]
	private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hWnd, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromRect(RECT lprc, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsZoomed(nint hWnd);

	[DllImport("user32.dll")]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

	private const int GWL_STYLE = -16;
	private const uint GW_OWNER = 4;

	// WS_CAPTION = WS_BORDER|WS_DLGFRAME；WS_THICKFRAME 可调整；WS_EX_TOOLWINDOW 工具窗（不进任务栏）
	private const long WS_CAPTION = 0x00C00000L;
	private const long WS_THICKFRAME = 0x00040000L;
	private const long WS_EX_TOOLWINDOW = 0x00000080L;
	private const long WS_CHILD = 0x40000000L;
	private const long WS_DISABLED = 0x08000000L;
	private const int DWMWA_CLOAKED = 14;

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern bool GetLayeredWindowAttributes(nint hWnd, out uint crKey, out byte bAlpha, out uint dwFlags);

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, EnumMonitorsProc lpfnEnum, nint dwData);

	private delegate bool EnumMonitorsProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

	private const int SW_RESTORE = 9;

	/// <summary>还原窗口但不激活（不抢前台焦点）；Win32 SW_SHOWNOACTIVATE。</summary>
	private const int SW_SHOWNOACTIVATE = 4;
	private const uint SWP_NOZORDER = 0x0004;
	private const uint SWP_NOMOVE = 0x0002;
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOACTIVATE = 0x0010;
	private const uint SWP_SHOWWINDOW = 0x0040;
	private const uint SWP_ASYNCWINDOWPOS = 0x4000;
	private static readonly nint HWND_TOP = new nint(0);
	private const uint WS_EX_LAYERED = 0x00080000;
	private const int GWL_EXSTYLE = -20;
	private const uint LWA_ALPHA = 0x00000002;
	private const uint LWA_COLORKEY = 0x00000001;
	private const uint SW_RESTORE_U = 9u;

	// 上次平铺快照（供「恢复」与内存记忆）。条目同时记录进程 PID：
	// HWND 会被系统复用，恢复时校验 PID 避免把旧坐标应用到无关窗口。
	private readonly struct TileSnapshotEntry
	{
		public readonly RECT Rect;
		public readonly uint Pid;

		public TileSnapshotEntry(RECT rect, uint pid)
		{
			Rect = rect;
			Pid = pid;
		}
	}

	private static readonly Dictionary<nint, TileSnapshotEntry> s_lastSnapshot = new Dictionary<nint, TileSnapshotEntry>();
	private static string s_currentLayout = "2L";

	/// <summary>可选布局 key 列表（顺序即编辑器下拉顺序）。</summary>
	public static List<string> LayoutKeys { get; } = new List<string>
	{
		"2L", "2T", "3L12", "3R21", "3R", "4G", "6G", "ML", "MR", "MT", "MB", "MO", "HS", "VS", "COL", "BSP", "AG"
	};

	/// <summary>轮换模式标记：参数为 Cycle 时循环切换布局。</summary>
	public const string CycleParam = "Cycle";

	/// <summary>反向轮换标记。</summary>
	public const string CycleBackParam = "CycleBack";

	/// <summary>还原标记：参数为 Restore 时还原所有窗口到平铺前。</summary>
	public const string RestoreParam = "Restore";

	/// <summary>主轴布局的 Master 占比（同 Dwalia 默认 0.6）。</summary>
	public const double MasterFactor = 0.6;

	public static bool IsValidLayout(string key)
	{
		return LayoutKeys.Contains(key);
	}

	public static string LayoutDisplayName(string key)
	{
		return key switch
		{
			"2L" => I18n.T("TileLayout2L"),
			"2T" => I18n.T("TileLayout2T"),
			"3L12" => I18n.T("TileLayout3L12"),
			"3R21" => I18n.T("TileLayout3R21"),
			"3R" => I18n.T("TileLayout3R"),
			"4G" => I18n.T("TileLayout4G"),
			"6G" => I18n.T("TileLayout6G"),
			"ML" => I18n.T("TileLayoutML"),
			"MR" => I18n.T("TileLayoutMR"),
			"MT" => I18n.T("TileLayoutMT"),
			"MB" => I18n.T("TileLayoutMB"),
			"MO" => I18n.T("TileLayoutMO"),
			"HS" => I18n.T("TileLayoutHS"),
			"VS" => I18n.T("TileLayoutVS"),
			"COL" => I18n.T("TileLayoutCOL"),
			"BSP" => I18n.T("TileLayoutBSP"),
			"AG" => I18n.T("TileLayoutAG"),
			_ => key
		};
	}

	/// <summary>循环范围：配置的 TileCycleLayouts（逗号分隔 key）；为空则全部布局参与。</summary>
	private static List<string> GetCycleScope()
	{
		List<string> list = new List<string>();
		string? cfg = ConfigManager.CurrentConfig?.TileCycleLayouts;
		if (!string.IsNullOrWhiteSpace(cfg))
		{
			foreach (string token in cfg.Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string k = token.Trim();
				if (IsValidLayout(k) && !list.Contains(k))
				{
					list.Add(k);
				}
			}
		}
		if (list.Count == 0)
		{
			list.AddRange(LayoutKeys);
		}
		return list;
	}

	/// <summary>当前布局（每次平铺/循环都会更新；供循环从"当前"起跳）。</summary>
	public static string CurrentLayout
	{
		get
		{
			return s_currentLayout;
		}
	}

	/// <summary>在后台执行平铺：取对象 → 各窗口主显示器工作区 → 按布局格子 SetWindowPos。</summary>
	public static void ExecuteTile(string? layoutKey)
	{
		try
		{
			string key = string.IsNullOrWhiteSpace(layoutKey) ? "2L" : layoutKey.Trim();
			if (string.Equals(key, RestoreParam, StringComparison.OrdinalIgnoreCase))
			{
				RestoreLastLayout();
				return;
			}
			List<string> scope = GetCycleScope();
			if (string.Equals(key, CycleParam, StringComparison.OrdinalIgnoreCase))
			{
				int cur = scope.IndexOf(s_currentLayout);
				if (cur < 0)
				{
					cur = 0;
				}
				key = scope[(cur + 1) % scope.Count];
			}
			else if (string.Equals(key, CycleBackParam, StringComparison.OrdinalIgnoreCase))
			{
				int cur = scope.IndexOf(s_currentLayout);
				if (cur < 0)
				{
					cur = 0;
				}
				key = scope[(cur - 1 + scope.Count) % scope.Count];
			}
			if (!IsValidLayout(key))
			{
				key = "2L";
			}
			s_currentLayout = key;
			// 排除名单（进程 exe 名，逗号/分号分隔）
			HashSet<string> exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string? excl = ConfigManager.CurrentConfig?.TileExcludeProcesses;
			if (!string.IsNullOrWhiteSpace(excl))
			{
				foreach (string token in excl.Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
				{
					exclude.Add(token.Trim().ToLowerInvariant());
				}
			}
			bool includeMinimized = ConfigManager.CurrentConfig?.TileIncludeMinimized == true;
			// 屏幕边距与窗口间距（物理像素，读取时夹紧防呆）
			int mt = Math.Clamp(ConfigManager.CurrentConfig?.TileMarginTop ?? 0, 0, 1000);
			int mb = Math.Clamp(ConfigManager.CurrentConfig?.TileMarginBottom ?? 0, 0, 1000);
			int ml = Math.Clamp(ConfigManager.CurrentConfig?.TileMarginLeft ?? 0, 0, 1000);
			int mr = Math.Clamp(ConfigManager.CurrentConfig?.TileMarginRight ?? 0, 0, 1000);
			int gap = Math.Clamp(ConfigManager.CurrentConfig?.TileGap ?? 0, 0, 500);
			int gapHalf = gap / 2;
			if (ml + mr + mt + mb + gap > 0)
			{
				AppLogger.LogInfo($"[Tile] margin L{ml}/T{mt}/R{mr}/B{mb}, gap={gap}");
			}
			List<nint> targets = GetTileTargets(exclude, includeMinimized);
			AppLogger.LogInfo($"[Tile] layout='{key}' targets={targets.Count}");
			if (targets.Count == 0)
			{
				return;
			}
			foreach (nint t in targets)
			{
				GetWindowThreadProcessId(t, out uint tp);
				AppLogger.LogInfo($"[Tile]   target hwnd=0x{t:X} exe={ExeNameOfPid(tp) ?? "?"} title=\"{TitleOf(t)}\"");
			}
			// 按显示器分区平铺：同一显示器上的窗口独立套用布局，
			// 避免"两窗分布两屏各占半屏"的不可预测行为。分区顺序 = 显示器在目标序列中的首次出现顺序。
			List<List<nint>> monitorGroups = new List<List<nint>>();
			Dictionary<nint, int> groupIndexByMonitor = new Dictionary<nint, int>();
			foreach (nint hwnd in targets)
			{
				nint mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
				if (!groupIndexByMonitor.TryGetValue(mon, out int gi))
				{
					gi = monitorGroups.Count;
					groupIndexByMonitor[mon] = gi;
					monitorGroups.Add(new List<nint>());
				}
				monitorGroups[gi].Add(hwnd);
			}

			Dictionary<nint, TileSnapshotEntry> snapshot = new Dictionary<nint, TileSnapshotEntry>();
			int globalIndex = 0;
			foreach (List<nint> group in monitorGroups)
			{
				// 固定布局超编：只平铺任务栏顺序前 nominal 个窗口，其余保持原样（不移动、不进快照）
				List<nint> groupWindows = group;
				int nominal = NominalCellCount(key);
				if (nominal > 0 && group.Count > nominal)
				{
					AppLogger.LogInfo($"[Tile] 布局 {key} 名义 {nominal} 格，本屏 {group.Count} 窗，按任务栏顺序取前 {nominal} 个，其余 {group.Count - nominal} 窗不参与");
					groupWindows = group.Take(nominal).ToList();
				}
				List<double[]> cells = LayoutCells(key, groupWindows.Count);
				int count = Math.Min(groupWindows.Count, cells.Count);
				for (int i = 0; i < count; i++)
				{
					nint hwnd = groupWindows[i];
					RECT wa = WorkAreaOf(hwnd);
					// 应用屏幕边距（收缩工作区，并夹紧防负尺寸）
					wa.Left += ml;
					wa.Top += mt;
					wa.Right = Math.Max(wa.Left + 1, wa.Right - mr);
					wa.Bottom = Math.Max(wa.Top + 1, wa.Bottom - mb);
					double[] c = cells[i];
					int x = wa.Left + (int)Math.Round((wa.Right - wa.Left) * c[0]);
					int y = wa.Top + (int)Math.Round((wa.Bottom - wa.Top) * c[1]);
					int w = (int)Math.Round((wa.Right - wa.Left) * c[2]) - (x - wa.Left);
					int h = (int)Math.Round((wa.Bottom - wa.Top) * c[3]) - (y - wa.Top);
					// 应用窗口间距：内边缘各收缩 gap/2，外边缘不缩（外圈留白已由边距控制）
					int gl = c[0] > 1e-9 ? gapHalf : 0;
					int gt = c[1] > 1e-9 ? gapHalf : 0;
					int gr = c[2] < 1 - 1e-9 ? gapHalf : 0;
					int gb = c[3] < 1 - 1e-9 ? gapHalf : 0;
					x += gl;
					y += gt;
					w -= gl + gr;
					h -= gt + gb;
					w = Math.Max(1, w);
					h = Math.Max(1, h);
					// 最小化/最大化会无视 SetWindowPos：先无激活还原，再捕获快照坐标
					// （顺序很重要：最小化窗口的 GetWindowRect 是 -32000 系，必须还原后读取）
					if (IsIconic(hwnd) || IsZoomed(hwnd))
					{
						ShowWindow(hwnd, SW_SHOWNOACTIVATE);
					}
					GetWindowRect(hwnd, out RECT before);
					GetWindowThreadProcessId(hwnd, out uint pid);
					bool ok = SetWindowPos(hwnd, HWND_TOP, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
					AppLogger.LogInfo($"[Tile] #{globalIndex} hwnd=0x{hwnd:X} exe={ExeNameOfPid(pid) ?? "?"} title=\"{TitleOf(hwnd)}\" rect=({x},{y},{w}x{h}) ok={ok}");
					globalIndex++;
					if (ok)
					{
						snapshot[hwnd] = new TileSnapshotEntry(before, pid);
					}
				}
			}
			lock (s_lastSnapshot)
			{
				// 清理已销毁窗口的陈旧条目（HWND 可能被系统复用，恢复时还需校验 PID）
				List<nint> dead = null;
				foreach (var kv in s_lastSnapshot)
				{
					if (!IsWindow(kv.Key))
					{
						(dead ??= new List<nint>()).Add(kv.Key);
					}
				}
				if (dead != null)
				{
					foreach (nint d in dead)
					{
						s_lastSnapshot.Remove(d);
					}
				}
				// 只记录"第一次"平铺前的状态：后续换布局/循环不覆盖，
				// 保证「还原所有窗口」始终回到首次平铺之前的样式。
				if (s_lastSnapshot.Count == 0)
				{
					foreach (var kv in snapshot)
					{
						s_lastSnapshot[kv.Key] = kv.Value;
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[Tile] 平铺失败", ex);
		}
	}

	/// <summary>恢复上次平铺前的窗口位置/大小（按需优先还原显示）。</summary>
	public static void RestoreLastLayout()
	{
		try
		{
			Dictionary<nint, TileSnapshotEntry> snap;
			lock (s_lastSnapshot)
			{
				snap = new Dictionary<nint, TileSnapshotEntry>(s_lastSnapshot);
			}
			AppLogger.LogInfo($"[Tile] restore targets={snap.Count}");
			foreach (var kv in snap)
			{
				try
				{
					// HWND 会被系统复用：窗口已销毁或 PID 不匹配（复用给了其他进程）时跳过，
					// 避免把旧坐标应用到无关窗口。
					if (!IsWindow(kv.Key))
					{
						continue;
					}
					GetWindowThreadProcessId(kv.Key, out uint pid);
					if (pid != kv.Value.Pid)
					{
						continue;
					}
					RECT r = kv.Value.Rect;
					if (IsIconic(kv.Key))
					{
						ShowWindow(kv.Key, SW_SHOWNOACTIVATE);
					}
					SetWindowPos(kv.Key, HWND_TOP, r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top),
						SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[Tile] 恢复失败", ex);
		}
	}

	/// <summary>内置"系统/厂商后台工具"黑名单（恒生效，避免干扰平铺）：输入法、UWP 宿主、设置、华硕等。</summary>
	private static readonly string[] s_systemUtilityExes =
	{
		"textinputhost", "ctfmon", "sihost", "startmenuexperiencehost", "searchhost", "searchapp",
		"shellexperiencehost", "runtimebroker", "backgroundtaskhost", "dllhost", "applicationframehost",
		"systemsettings", "ashotplugctrl", "asmonitorcontrol", "armourycrateservice", "asusappservice",
		"hhddevicediscovery", "igfxext", "igfxpers", "hkcmd"
	};

	/// <summary>
	/// 平铺对象：当前虚拟桌面上的可见窗口。只管理"exe 出现在任务栏槽位"的应用窗口（Dwalia 语义：
	/// 只处理真实驻留应用），同 exe 多窗口全收（两个终端都参与）；组间顺序 = 任务栏槽位顺序，
	/// 组内按句柄升序（≈先启动在前，先启动者为 Master）。
	/// 排除：无标题栏/工具窗/透明/遮蔽/无拥有者/无标题/本进程/系统工具黑名单/用户排除名单/非任务栏应用的杂窗。
	/// </summary>
	private static List<nint> GetTileTargets(HashSet<string> userExcludedExes, bool includeMinimized)
	{
		List<nint> result = new List<nint>();
		uint selfPid = (uint)Environment.ProcessId;

		// 黑名单 = 内置系统工具 + 用户配置的排除名单
		HashSet<string> blockedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		blockedExes.UnionWith(s_systemUtilityExes);
		if (userExcludedExes.Count > 0)
		{
			blockedExes.UnionWith(userExcludedExes);
		}

		// 槽位顺序 → exe 名顺序（分组排序基准 + 白名单：只收这些 exe 的窗口）
		List<string> slotExeOrder = new List<string>();
		HashSet<string> slotExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (nint slot in WindowTaskbarHelper.GetTaskbarOrderedWindows())
		{
			GetWindowThreadProcessId(slot, out uint pid);
			string? exe = ExeNameOfPid(pid);
			if (exe != null && slotExes.Add(exe))
			{
				slotExeOrder.Add(exe);
			}
		}

		// 同 exe 分组：exe(lower) -> hwnds
		Dictionary<string, List<nint>> groups = new Dictionary<string, List<nint>>(StringComparer.OrdinalIgnoreCase);
		int blockedCount = 0;
		foreach (nint h in EnumerateAllTopLevelWindows())
		{
			try
			{
				if (!IsWindowVisible(h))
				{
					continue;
				}
				if (!includeMinimized && IsIconic(h))
				{
					continue;
				}
				if (!WindowTaskbarHelper.IsOnCurrentVirtualDesktop(h))
				{
					continue;
				}
				// 只收"标准应用窗口"：有标题栏、可调整、非工具窗、无拥有者（丢弃不进任务栏的后台/工具/对话框）
				long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
				if ((style & WS_CAPTION) == 0L || (style & WS_THICKFRAME) == 0L)
				{
					continue;
				}
				// 学 Dwalia：WS_CHILD / WS_DISABLED / 工具窗 / 特殊类名 / DWM 遮蔽(CLOAKED) 一律不进布局
				if ((style & WS_CHILD) != 0L || (style & WS_DISABLED) != 0L)
				{
					continue;
				}
				string winCls = GetClassNameSafe(h);
				if (winCls == "Progman" || winCls == "WorkerW" || winCls == "Shell_TrayWnd" || winCls == "Shell_SecondaryTrayWnd" || winCls == "Windows.UI.Core.CoreWindow" || winCls == "ApplicationFrameWindow")
				{
					continue;
				}
				if (IsWindowCloaked(h))
				{
					continue;
				}
				long exstyle = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
				if ((exstyle & WS_EX_TOOLWINDOW) != 0L)
				{
					continue;
				}
				// 排除透明/半透明窗口（分层且 alpha<255 或 COLORKEY），避免"透明窗占位"
				if ((exstyle & WS_EX_LAYERED) != 0L &&
					GetLayeredWindowAttributes(h, out uint crKey, out byte alpha, out uint lwaFlags) &&
					(((lwaFlags & LWA_ALPHA) != 0u && alpha < 255) || (lwaFlags & LWA_COLORKEY) != 0u))
				{
					continue;
				}
				if (GetWindow(h, GW_OWNER) != IntPtr.Zero)
				{
					continue;
				}
				GetWindowThreadProcessId(h, out uint pid);
				if (pid == selfPid)
				{
					continue;
				}
				string? exeKey = ExeNameOfPid(pid);
				// 只收任务栏存在应用的窗口（Dwalia：只管理真实驻留应用的窗口），且不在系统工具/用户黑名单
				if (exeKey == null || !slotExes.Contains(exeKey) || blockedExes.Contains(exeKey))
				{
					if (exeKey != null && blockedExes.Contains(exeKey))
					{
						blockedCount++;
					}
					continue;
				}
				StringBuilder sb = new StringBuilder(128);
				if (GetWindowText(h, sb, 128) <= 0)
				{
					continue;
				}
				if (!groups.TryGetValue(exeKey, out List<nint>? g))
				{
					g = new List<nint>();
					groups[exeKey] = g;
					g.Add(h);
				}
				else
				{
					g.Add(h);
				}
			}
			catch
			{
			}
		}
		foreach (List<nint> g in groups.Values)
		{
			g.Sort(); // 组内句柄升序（先启动在前）
		}
		// 按槽位顺序输出组，再输出未出现在槽位的组（按其最小句柄排序），最后无 exe 的
		HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string exe in slotExeOrder)
		{
			if (groups.TryGetValue(exe, out List<nint>? g))
			{
				result.AddRange(g);
				emitted.Add(exe);
			}
		}
		List<string> rest = groups.Keys.Where((string k) => !emitted.Contains(k)).OrderBy((string k) => groups[k][0]).ToList();
		foreach (string exe in rest)
		{
			result.AddRange(groups[exe]);
		}

		if (result.Count == 0)
		{
			// 主路径（任务栏白名单 + 严格过滤）全军覆没：回退弱过滤兜底。
			// 该路径过滤语义远弱于主路径，必须显式留痕便于排查。
			AppLogger.LogWarn("[Tile] 主过滤路径无目标，回退弱过滤兜底枚举（VDM 不可用或所有窗口被过滤）");
			result.AddRange(EnumerateTopLevelWindows(includeMinimized));
		}
		// 诊断：被黑名单/杂窗挡掉的数量（用于核对目标数与真实窗口数）
		AppLogger.LogInfo($"[Tile]   blocked={blockedCount}");
		return result;
	}

	/// <summary>窗口是否被 DWM 遮蔽（虚拟桌面隐藏/最小化过渡等隐形态）——Dwalia 的 Cloaked 判定。</summary>
	private static bool IsWindowCloaked(nint hWnd)
	{
		try
		{
			return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0;
		}
		catch
		{
			return false;
		}
	}

	private static string GetClassNameSafe(nint hWnd)
	{
		try
		{
			StringBuilder sb = new StringBuilder(256);
			return GetClassName(hWnd, sb, 256) > 0 ? sb.ToString() : "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>进程 exe 名（小写、无扩展名）；失败 null。轻量缓存避免重复 OpenProcess。</summary>
	private static string? ExeNameOfPid(uint pid)
	{
		string key = pid.ToString();
		if (s_exeNameCache.TryGetValue(key, out string? cached))
		{
			return cached;
		}
		string? exe = WindowTaskbarHelper.GetProcessImageNameByPid(pid);
		string? lower = exe == null ? null : System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
		if (s_exeNameCache.Count > 256)
		{
			s_exeNameCache.Clear();
		}
		s_exeNameCache[key] = lower;
		return lower;
	}

	/// <summary>读取窗口标题（日志用，截断 128 字符，失败返回空串）。</summary>
	private static string TitleOf(nint hWnd)
	{
		try
		{
			if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
			{
				return "";
			}
			StringBuilder sb = new StringBuilder(128);
			return GetWindowText(hWnd, sb, 128) > 0 ? sb.ToString() : "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>EnumWindows 全量枚举顶层窗口（不过滤，便于分组；注意通过枚举回调收集）。</summary>
	private static List<nint> EnumerateAllTopLevelWindows()
	{
		List<nint> buffer = new List<nint>();
		List<nint> previous = s_enumBuffer;
		s_enumBuffer = buffer;
		try
		{
			EnumWindows(s_enumProc, IntPtr.Zero);
		}
		catch
		{
		}
		finally
		{
			s_enumBuffer = previous;
		}
		return buffer;
	}

	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	private static readonly EnumWindowsProc s_enumProc = EnumCallback;

	private static List<nint> s_enumBuffer = new List<nint>();

	private static readonly Dictionary<string, string?> s_exeNameCache = new Dictionary<string, string?>();

	private static bool EnumCallback(nint hWnd, nint lParam)
	{
		s_enumBuffer.Add(hWnd);
		return true;
	}

	// ===== 启动后窗口定位辅助（供 ActionExecutor.Launch 兜底使用）=====

	private const int SW_MAXIMIZE = 3;

	/// <summary>启动后新窗口筛选时忽略的系统/壳层窗口类名。</summary>
	private static readonly HashSet<string> s_launchIgnoredClasses = new HashSet<string>(StringComparer.Ordinal)
	{
		"Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
		"tooltips_class32", "#32768", "ForegroundStaging", "NotifyIconOverflowWindow",
		"Windows.UI.Core.CoreWindow", "Xaml_WindowedPopupClass", "TaskListThumbnailWnd",
		"XamlExplorerHostIslandWindow", "Windows.Internal.Shell.TabProxyWindow"
	};

	/// <summary>快照当前所有顶层窗口句柄（含不可见），用于启动后比对新出现的窗口。线程安全（不使用共享枚举缓冲）。</summary>
	public static HashSet<nint> SnapshotTopLevelWindows()
	{
		HashSet<nint> set = new HashSet<nint>();
		try
		{
			EnumWindows(delegate(nint h, nint l)
			{
				set.Add(h);
				return true;
			}, IntPtr.Zero);
		}
		catch
		{
		}
		return set;
	}

	/// <summary>
	/// 判断窗口是否为值得搬移的应用主窗口（排除本进程、子窗口、工具窗与壳层/弹出类窗口）。
	/// requireVisible=false 用于窗口刚创建尚未显示的场景：此时标题可能为空，改用外观样式判断。
	/// </summary>
	public static bool IsCandidateAppWindow(nint h, bool requireVisible)
	{
		try
		{
			if (h == IntPtr.Zero || !IsWindow(h))
			{
				return false;
			}
			if (requireVisible && (!IsWindowVisible(h) || IsWindowCloaked(h)))
			{
				return false;
			}
			GetWindowThreadProcessId(h, out uint pid);
			if (pid == (uint)Environment.ProcessId)
			{
				return false;
			}
			long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
			if ((style & WS_CHILD) != 0L)
			{
				return false;
			}
			long exStyle = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
			if ((exStyle & WS_EX_TOOLWINDOW) != 0L)
			{
				return false;
			}
			string cls = GetClassNameSafe(h);
			if (s_launchIgnoredClasses.Contains(cls))
			{
				return false;
			}
			if (requireVisible)
			{
				return TitleOf(h).Length > 0;
			}
			return (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0L || cls == "ApplicationFrameWindow";
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 找出快照之后新出现的、可见且带标题的应用窗口。
	/// </summary>
	public static List<nint> FindNewAppWindows(HashSet<nint> snapshot)
	{
		List<nint> result = new List<nint>();
		try
		{
			EnumWindows(delegate(nint h, nint l)
			{
				if (!snapshot.Contains(h) && IsCandidateAppWindow(h, requireVisible: true))
				{
					result.Add(h);
				}
				return true;
			}, IntPtr.Zero);
		}
		catch
		{
		}
		return result;
	}

	/// <summary>
	/// 若窗口不在目标显示器上，则按其在当前屏工作区的相对位置比例平移到目标显示器工作区（保持尺寸、不抢焦点）。
	/// 最大化窗口会先还原、移动后再最大化。返回 true 表示执行了移动。
	/// </summary>
	public static bool EnsureWindowOnMonitor(nint hWnd, nint hTargetMonitor)
	{
		try
		{
			if (hWnd == IntPtr.Zero || hTargetMonitor == IntPtr.Zero || !IsWindow(hWnd))
			{
				return false;
			}
			nint curMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
			if (curMon == hTargetMonitor)
			{
				return false;
			}
			MONITORINFO mi = default;
			mi.cbSize = Marshal.SizeOf<MONITORINFO>();
			if (!GetMonitorInfo(hTargetMonitor, ref mi))
			{
				return false;
			}
			RECT nw = mi.rcWork;
			// 尚未显示的窗口（创建阶段搬移）：不还原、不加 SWP_SHOWWINDOW，避免提前把它显示出来
			bool visible = IsWindowVisible(hWnd);
			bool wasZoomed = visible && IsZoomed(hWnd);
			if (wasZoomed || (visible && IsIconic(hWnd)))
			{
				ShowWindow(hWnd, SW_RESTORE);
			}
			RECT cur;
			GetWindowRect(hWnd, out cur);
			if (cur.Right - cur.Left <= 0 || cur.Bottom - cur.Top <= 0)
			{
				return false; // 空矩形的隐藏辅助窗口，无需搬移
			}
			RECT curWork = WorkAreaOf(hWnd);
			int curW = Math.Max(1, curWork.Right - curWork.Left);
			int curH = Math.Max(1, curWork.Bottom - curWork.Top);
			int fx = cur.Left - curWork.Left;
			int fy = cur.Top - curWork.Top;
			int targetW = Math.Max(1, nw.Right - nw.Left);
			int targetH = Math.Max(1, nw.Bottom - nw.Top);
			int w = Math.Min(Math.Max(1, cur.Right - cur.Left), targetW);
			int h = Math.Min(Math.Max(1, cur.Bottom - cur.Top), targetH);
			int x = nw.Left + (int)Math.Round((double)fx * targetW / curW);
			int y = nw.Top + (int)Math.Round((double)fy * targetH / curH);
			// 防止按比例映射后溢出目标工作区
			x = Math.Max(nw.Left, Math.Min(x, nw.Right - w));
			y = Math.Max(nw.Top, Math.Min(y, nw.Bottom - h));
			uint flags = SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : 0u);
			SetWindowPos(hWnd, HWND_TOP, x, y, w, h, flags);
			if (wasZoomed)
			{
				ShowWindow(hWnd, SW_MAXIMIZE);
			}
			AppLogger.LogInfo($"[Launch] EnsureWindowOnMonitor: hwnd=0x{hWnd:X} '{TitleOf(hWnd)}' [{GetClassNameSafe(hWnd)}] {(visible ? "visible" : "hidden")} -> ({x},{y},{w}x{h}){(wasZoomed ? " maximized" : "")}");
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogWarn($"[Launch] EnsureWindowOnMonitor failed for 0x{hWnd:X}: {ex.Message}");
			return false;
		}
	}

	/// <summary>把当前前台窗口平移到下一台显示器（保持相对位置与尺寸）。</summary>
	public static void MoveWindowToNextMonitor()
	{
		try
		{
			nint fg = GetForegroundWindow();
			if (fg == IntPtr.Zero || fg == GetShellWindowHandleSafe())
			{
				return;
			}
			uint fgPid;
			GetWindowThreadProcessId(fg, out fgPid);
			if (fgPid == (uint)Environment.ProcessId)
			{
				return; // 不动自己的窗口
			}
			RECT cur;
			GetWindowRect(fg, out cur);
			RECT curWork = WorkAreaOf(fg);
			List<RECT> monitors = new List<RECT>();
			EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(nint hMon, nint hdc, ref RECT rc, nint data)
			{
				monitors.Add(rc);
				return true;
			}, IntPtr.Zero);
			if (monitors.Count < 2)
			{
				return; // 单显示器无可移动
			}
			// 找到当前所在屏，取下一个（循环）
			int curIdx = -1;
			for (int i = 0; i < monitors.Count; i++)
			{
				RECT m = monitors[i];
				if (cur.Left >= m.Left && cur.Left < m.Right && cur.Top >= m.Top && cur.Top < m.Bottom)
				{
					curIdx = i;
					break;
				}
			}
			int next = (curIdx + 1) % monitors.Count;
			RECT nm = monitors[next];
			RECT nw = WorkAreaOfMonitor(nm);
			// 相对当前屏工作区的比例 → 目标屏工作区
			int curW = Math.Max(1, curWork.Right - curWork.Left);
			int curH = Math.Max(1, curWork.Bottom - curWork.Top);
			int fx = cur.Left - curWork.Left;
			int fy = cur.Top - curWork.Top;
			int targetW = Math.Max(1, nw.Right - nw.Left);
			int targetH = Math.Max(1, nw.Bottom - nw.Top);
			int x = nw.Left + (int)Math.Round((double)fx * targetW / curW);
			int y = nw.Top + (int)Math.Round((double)fy * targetH / curH);
			int w = Math.Max(1, cur.Right - cur.Left);
			int h = Math.Max(1, cur.Bottom - cur.Top);
			w = Math.Min(w, targetW);
			h = Math.Min(h, targetH);
			if (IsIconic(fg))
			{
				ShowWindow(fg, SW_RESTORE);
			}
			SetWindowPos(fg, HWND_TOP, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
			AppLogger.LogInfo($"[Tile] MoveToMonitor: hwnd=0x{fg:X} → monitor#{next} ({x},{y},{w}x{h})");
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[Tile] 移动显示器失败", ex);
		}
	}

	private static nint GetShellWindowHandleSafe()
	{
		try
		{
			return GetShellWindow();
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static RECT WorkAreaOfMonitor(RECT monitor)
	{
		try
		{
			nint mon = MonitorFromRect(monitor, MONITOR_DEFAULTTONEAREST);
			MONITORINFO mi = default;
			mi.cbSize = Marshal.SizeOf<MONITORINFO>();
			if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
			{
				return mi.rcWork;
			}
		}
		catch
		{
		}
		return monitor;
	}

	/// <summary>切换当前前台窗口置顶状态；参数 "1"/"on" 强制置顶、"0"/"off" 取消、空参数切换。</summary>
	public static void ToggleWindowTopmost(string? param)
	{
		try
		{
			nint fg = GetForegroundWindow();
			if (fg == IntPtr.Zero || fg == GetShellWindowHandleSafe())
			{
				return;
			}
			uint fgPid;
			GetWindowThreadProcessId(fg, out fgPid);
			if (fgPid == (uint)Environment.ProcessId)
			{
				return;
			}
			bool top = (GetWindowLongPtr(fg, GWL_EXSTYLE).ToInt64() & 0x8L) != 0L; // WS_EX_TOPMOST
			bool want = string.IsNullOrWhiteSpace(param) ? !top : (param.Trim() == "1" || string.Equals(param.Trim(), "on", StringComparison.OrdinalIgnoreCase));
			SetWindowPos(fg, want ? new nint(-1) : new nint(-2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
			AppLogger.LogInfo($"[Tile] Topmost hwnd=0x{fg:X} → {want}");
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[Tile] 置顶失败", ex);
		}
	}

	/// <summary>设置当前前台窗口透明度（参数 1~100，空参数默认 50；100 = 不透明）。</summary>
	public static void SetWindowOpacity(string? param)
	{
		try
		{
			nint fg = GetForegroundWindow();
			if (fg == IntPtr.Zero || fg == GetShellWindowHandleSafe())
			{
				return;
			}
			uint fgPid;
			GetWindowThreadProcessId(fg, out fgPid);
			if (fgPid == (uint)Environment.ProcessId)
			{
				return;
			}
			double level = 50.0;
			if (double.TryParse(param, out double v))
			{
				level = v;
			}
			level = Math.Max(1.0, Math.Min(100.0, level));
			nint ex = GetWindowLongPtr(fg, GWL_EXSTYLE);
			SetWindowLongPtr(fg, GWL_EXSTYLE, new nint(ex.ToInt64() | WS_EX_LAYERED));
			SetLayeredWindowAttributes(fg, 0, (byte)Math.Round(level * 255.0 / 100.0), LWA_ALPHA);
			AppLogger.LogInfo($"[Tile] Opacity hwnd=0x{fg:X} → {level}%");
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[Tile] 透明度失败", ex);
		}
	}

	/// <summary>EnumWindows 兜底枚举：可见、非本进程、有标题的顶层窗口（保留任务栏顺序路径的过滤语义）。</summary>
	private static List<nint> EnumerateTopLevelWindows(bool includeMinimized)
	{
		uint selfPid = (uint)Environment.ProcessId;
		List<nint> buffer = new List<nint>();
		List<nint> previous = s_enumBuffer;
		s_enumBuffer = buffer;
		try
		{
			EnumWindows(s_enumProc, IntPtr.Zero);
		}
		catch
		{
		}
		finally
		{
			s_enumBuffer = previous;
		}
		List<nint> found = new List<nint>();
		foreach (nint h in buffer)
		{
			try
			{
				if (h == IntPtr.Zero || !IsWindowVisible(h))
				{
					continue;
				}
				// 与主路径语义对齐：仅当未开启「包含最小化窗口」时才跳过最小化窗口
				if (!includeMinimized && IsIconic(h))
				{
					continue;
				}
				GetWindowThreadProcessId(h, out uint pid);
				if (pid == selfPid)
				{
					continue;
				}
				StringBuilder sb = new StringBuilder(128);
				if (GetWindowText(h, sb, 128) <= 0)
				{
					continue;
				}
				found.Add(h);
			}
			catch
			{
			}
		}
		return found;
	}

	private static RECT WorkAreaOf(nint hWnd)
	{
		try
		{
			nint mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
			MONITORINFO mi = default;
			mi.cbSize = Marshal.SizeOf<MONITORINFO>();
			if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
			{
				return mi.rcWork;
			}
		}
		catch
		{
		}
		return new RECT
		{
			Left = (int)SystemParameters.VirtualScreenLeft,
			Top = (int)SystemParameters.VirtualScreenTop,
			Right = (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth),
			Bottom = (int)(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
		};
	}

	/// <summary>
/// 生成布局格子比例 [x0,y0,x1,y1]，格子顺序 = 窗口顺序。
/// 固定预设为常量表；ML/MR/MT/MB（Master+Stack，学习 Dwalia 算法）按窗口数动态切分：
/// Master 占 60%（Dwalia 默认 masterFactor），其余窗口在 Stack 区按 n-1 等分。
/// </summary>
	private static List<double[]> LayoutCells(string key, int n)
	{
		switch (key)
		{
		case "ML":
			return MasterCells(n, MasterFactor, masterLeft: true, masterTop: false);
		case "MR":
			return MasterCells(n, MasterFactor, masterLeft: false, masterTop: false);
		case "MT":
			return MasterCells(n, MasterFactor, masterLeft: false, masterTop: true);
		case "MB":
			return MasterCells(n, MasterFactor, masterLeft: false, masterTop: true, masterBottom: true);
		case "HS":
			return MasterCells(n, 0.5, masterLeft: false, masterTop: true); // HorizontalStack：上主 50% + 下栈单行
		case "VS":
			return MasterCells(n, 0.5, masterLeft: true, masterTop: false); // VerticalStack：左主 50% + 右栈单列
		case "MO":
			return MonocleCells(n); // Monocle：每窗占满工作区
		case "COL":
			return ColumnsCells(n); // Columns：等宽竖列
		case "BSP":
			return BspCells(n);     // BSP：交替二分递归
		case "AG":
			return AutoGridCells(n); // AutoGrid：按数量自适应行列
		}
		List<double[]> fixedCells = new List<double[]>();
		// 固定网格布局：窗口数等于名义格数时用其标准形状；
		// 缺编（n < 名义）时回退占满网格（行优先 + 末行摊平），窗口铺满整个工作区不留空位；
		// 超编（n > 名义）由 ExecuteTile 在分组层截断到名义格数，此处恒有 n <= nominal。
		int nominal = NominalCellCount(key);
		if (nominal > 0 && n < nominal)
		{
			return FillGridCells(n);
		}
		switch (key)
		{
		case "2L":
			fixedCells.Add(new[] { 0.0, 0.0, 0.5, 1.0 });
			fixedCells.Add(new[] { 0.5, 0.0, 1.0, 1.0 });
			break;
		case "2T":
			fixedCells.Add(new[] { 0.0, 0.0, 1.0, 0.5 });
			fixedCells.Add(new[] { 0.0, 0.5, 1.0, 1.0 });
			break;
		case "3L12":
			fixedCells.Add(new[] { 0.0, 0.0, 0.5, 1.0 });
			fixedCells.Add(new[] { 0.5, 0.0, 1.0, 0.5 });
			fixedCells.Add(new[] { 0.5, 0.5, 1.0, 1.0 });
			break;
		case "3R21":
			fixedCells.Add(new[] { 0.0, 0.0, 0.5, 0.5 });
			fixedCells.Add(new[] { 0.0, 0.5, 0.5, 1.0 });
			fixedCells.Add(new[] { 0.5, 0.0, 1.0, 1.0 });
			break;
		case "3R":
			fixedCells.Add(new[] { 0.0, 0.0, 1.0 / 3.0, 1.0 });
			fixedCells.Add(new[] { 1.0 / 3.0, 0.0, 2.0 / 3.0, 1.0 });
			fixedCells.Add(new[] { 2.0 / 3.0, 0.0, 1.0, 1.0 });
			break;
		case "4G":
			fixedCells.Add(new[] { 0.0, 0.0, 0.5, 0.5 });
			fixedCells.Add(new[] { 0.5, 0.0, 1.0, 0.5 });
			fixedCells.Add(new[] { 0.0, 0.5, 0.5, 1.0 });
			fixedCells.Add(new[] { 0.5, 0.5, 1.0, 1.0 });
			break;
		case "6G":
			fixedCells.Add(new[] { 0.0, 0.0, 1.0 / 3.0, 0.5 });
			fixedCells.Add(new[] { 1.0 / 3.0, 0.0, 2.0 / 3.0, 0.5 });
			fixedCells.Add(new[] { 2.0 / 3.0, 0.0, 1.0, 0.5 });
			fixedCells.Add(new[] { 0.0, 0.5, 1.0 / 3.0, 1.0 });
			fixedCells.Add(new[] { 1.0 / 3.0, 0.5, 2.0 / 3.0, 1.0 });
			fixedCells.Add(new[] { 2.0 / 3.0, 0.5, 1.0, 1.0 });
			break;
		default:
			fixedCells.Add(new[] { 0.0, 0.0, 0.5, 1.0 });
			fixedCells.Add(new[] { 0.5, 0.0, 1.0, 1.0 });
			break;
		}
		return fixedCells;
	}

	/// <summary>Monocle：所有窗口占满同一工作区（层叠，最后者在上）。</summary>
	private static List<double[]> MonocleCells(int n)
	{
		List<double[]> cells = new List<double[]>();
		for (int i = 0; i < n; i++)
		{
			cells.Add(new[] { 0.0, 0.0, 1.0, 1.0 });
		}
		return cells;
	}

	/// <summary>固定布局的名义格数（2L/2T=2，3L12/3R21/3R=3，4G=4，6G=6；动态布局返回 0 表示不限）。</summary>
	private static int NominalCellCount(string key)
	{
		return key switch
		{
			"2L" => 2,
			"2T" => 2,
			"3L12" => 3,
			"3R21" => 3,
			"3R" => 3,
			"4G" => 4,
			"6G" => 6,
			_ => 0
		};
	}

	/// <summary>
	/// 占满网格：任意 n 个窗口铺满整个工作区、无空位。
	/// 行优先填充（cols=⌈√n⌉），最后一行不足 cols 个窗口时摊平整行宽度。
	/// 例：n=2 → 左右对半；n=3 → 上排 2 窗各半宽 + 下排 1 窗全宽；
	/// n=5 → 上排 3 窗 + 下排 2 窗（各 1.5 倍宽）；n=6 → 标准 2×3 六宫格。
	/// </summary>
	private static List<double[]> FillGridCells(int n)
	{
		List<double[]> cells = new List<double[]>();
		if (n <= 0)
		{
			return cells;
		}
		int cols = (int)Math.Ceiling(Math.Sqrt(n));
		int rows = (n + cols - 1) / cols;
		for (int i = 0; i < n; i++)
		{
			int r = i / cols;
			int c = i % cols;
			// 最后一行不足 cols 个时，按实际窗口数摊平行宽，保证铺满且无空位
			bool isLastRow = r == rows - 1;
			int rowCols = isLastRow ? n - (rows - 1) * cols : cols;
			cells.Add(new[] { (double)c / rowCols, (double)r / rows, (double)(c + 1) / rowCols, (double)(r + 1) / rows });
		}
		return cells;
	}

	/// <summary>Columns：等宽竖列，每窗一列。</summary>
	private static List<double[]> ColumnsCells(int n)
	{
		List<double[]> cells = new List<double[]>();
		for (int i = 0; i < n; i++)
		{
			cells.Add(new[] { (double)i / n, 0.0, (double)(i + 1) / n, 1.0 });
		}
		return cells;
	}

	/// <summary>BSP：交替二分递归——第 i 层取当前区块一半，其余窗口递归剩余区块。</summary>
	private static List<double[]> BspCells(int n)
	{
		List<double[]> cells = new List<double[]>();
		BspSplit(cells, 0.0, 0.0, 1.0, 1.0, 0, n);
		return cells;
	}

	private static void BspSplit(List<double[]> cells, double x0, double y0, double x1, double y1, int i, int n)
	{
		if (i >= n)
		{
			return;
		}
		if (i == n - 1)
		{
			cells.Add(new[] { x0, y0, x1, y1 });
			return;
		}
		if (i % 2 == 0)
		{
			double mx = (x0 + x1) / 2.0;
			cells.Add(new[] { x0, y0, mx, y1 });
			BspSplit(cells, mx, y0, x1, y1, i + 1, n);
		}
		else
		{
			double my = (y0 + y1) / 2.0;
			cells.Add(new[] { x0, y0, x1, my });
			BspSplit(cells, x0, my, x1, y1, i + 1, n);
		}
	}

	/// <summary>AutoGrid：按窗口数自适应行列（cols=⌈√n⌉）。</summary>
	private static List<double[]> AutoGridCells(int n)
	{
		List<double[]> cells = new List<double[]>();
		if (n <= 0)
		{
			return cells;
		}
		int cols = (int)Math.Ceiling(Math.Sqrt(n));
		int rows = (n + cols - 1) / cols;
		for (int i = 0; i < n; i++)
		{
			int r = i / cols;
			int c = i % cols;
			cells.Add(new[] { (double)c / cols, (double)r / rows, (double)(c + 1) / cols, (double)(r + 1) / rows });
		}
		return cells;
	}

	/// <summary>Master+Stack 动态格子：Master 占 factor，Stack 区等分 n-1 块（Master 恒为 0 号窗口）。</summary>
	private static List<double[]> MasterCells(int n, double factor, bool masterLeft, bool masterTop, bool masterBottom = false)
	{
		List<double[]> cells = new List<double[]>();
		if (n <= 0)
		{
			return cells;
		}
		if (n == 1)
		{
			cells.Add(new[] { 0.0, 0.0, 1.0, 1.0 });
			return cells;
		}
		int stack = n - 1;
		if (masterLeft || !masterTop)
		{
			// 左右主轴：Master 占 factor（左或右），Stack 侧列按行等分
			double mX0 = masterLeft ? 0.0 : 1.0 - factor;
			double mX1 = masterLeft ? factor : 1.0;
			double sX0 = masterLeft ? factor : 0.0;
			double sX1 = masterLeft ? 1.0 : 1.0 - factor;
			cells.Add(new[] { mX0, 0.0, mX1, 1.0 });
			for (int i = 0; i < stack; i++)
			{
				cells.Add(new[] { sX0, (double)i / stack, sX1, (double)(i + 1) / stack });
			}
			return cells;
		}
		// 上下主轴：Master 占 factor（顶部或底部），Stack 横排按列等分
		double mY0 = masterBottom ? 1.0 - factor : 0.0;
		double mY1 = masterBottom ? 1.0 : factor;
		double sY0 = masterBottom ? 0.0 : factor;
		double sY1 = masterBottom ? 1.0 - factor : 1.0;
		cells.Add(new[] { 0.0, mY0, 1.0, mY1 });
		for (int i = 0; i < stack; i++)
		{
			cells.Add(new[] { (double)i / stack, sY0, (double)(i + 1) / stack, sY1 });
		}
		return cells;
	}
}