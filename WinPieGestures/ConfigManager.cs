using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace WinPieGestures;

public static class ConfigManager
{
	private static readonly string AppDataFolder;

	private static readonly string ConfigPath;

	public static AppConfig CurrentConfig { get; internal set; }

	private static long _configurationRevision;

	/// <summary>
	/// 轮盘可见配置的单调修订号。设置页发生内存态修改时立即递增，保存/导入/重新加载时也递增，
	/// 供长期复用的 RadialWindow 判断是否需要重建视觉树。
	/// </summary>
	public static long ConfigurationRevision => Interlocked.Read(ref _configurationRevision);

	public static void MarkConfigurationChanged()
	{
		Interlocked.Increment(ref _configurationRevision);
	}

	// 本次启动是否因配置文件损坏而回落到了默认配置。
	// 为 true 时必须禁止任何自动写盘，否则会把默认配置覆盖掉用户尚可恢复的损坏文件。
	public static bool IsFallbackConfig { get; private set; }

	private static string GetAppDataFolder()
	{
		string path = (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOCALAPPDATA")) ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : Environment.GetEnvironmentVariable("LOCALAPPDATA"));
		string text = Path.Combine(path, "StarPie");
		string text2 = Path.Combine(path, "WinPieGestures");
		if (!Directory.Exists(text) && Directory.Exists(text2))
		{
			try
			{
				Directory.CreateDirectory(text);
				string text3 = Path.Combine(text2, "config.json");
				string text4 = Path.Combine(text, "config.json");
				if (File.Exists(text3) && !File.Exists(text4))
				{
					File.Copy(text3, text4);
				}
			}
			catch
			{
			}
		}
		return text;
	}

	// 保留无法解析的配置文件现场，使用户有机会手工恢复，而不是被默认配置静默覆盖。
	private static void BackupCorruptConfig()
	{
		try
		{
			if (!File.Exists(ConfigPath))
			{
				return;
			}
			string destFileName = ConfigPath + ".corrupt." + DateTime.Now.ToString("yyyyMMddHHmmss");
			File.Copy(ConfigPath, destFileName, overwrite: true);
			AppLogger.LogInfo("Backed up unreadable config to '" + destFileName + "'");
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to back up unreadable config", ex);
		}
	}

	static ConfigManager()
	{
		AppDataFolder = GetAppDataFolder();
		ConfigPath = Path.Combine(AppDataFolder, "config.json");
		LoadConfig();
	}

	public static void LoadConfig()
	{
		try
		{
			if (!Directory.Exists(AppDataFolder))
			{
				Directory.CreateDirectory(AppDataFolder);
			}
			if (File.Exists(ConfigPath))
			{
				string json = File.ReadAllText(ConfigPath);
				bool hasLegacyBase64 = json.Contains("\"EmbeddedCustomIcons\"", StringComparison.OrdinalIgnoreCase) ||
				                       json.Contains("data:image/", StringComparison.OrdinalIgnoreCase);
				JsonSerializerOptions options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					AllowTrailingCommas = true,
					ReadCommentHandling = JsonCommentHandling.Skip
				};
				CurrentConfig = JsonSerializer.Deserialize<AppConfig>(json, options) ?? CreateDefaultConfig();
				EnsureConfigHealth(CurrentConfig);
				AppLogger.LogInfo($"Loaded configuration from '{ConfigPath}'");
				if (hasLegacyBase64)
				{
					SaveConfig();
					AppLogger.LogInfo("Automatically purged legacy Base64 embedded data from config file.");
				}
			}
			else
			{
				CurrentConfig = CreateDefaultConfig();
				EnsureConfigHealth(CurrentConfig);
				SaveConfig();
				AppLogger.LogInfo($"Created and saved default configuration at '{ConfigPath}'");
			}
			I18n.SetLanguage(CurrentConfig.Language);
			MarkConfigurationChanged();
			EnsureConfigsFolder();
			// 启动性能优化：自启同步完全移出启动关键路径，后台延迟 4 秒执行，消除开机时的阻塞
			_ = System.Threading.Tasks.Task.Run(async () =>
			{
				try
				{
					await System.Threading.Tasks.Task.Delay(4000).ConfigureAwait(false);
					EnsureAutoStartRegistryUpToDate();
				}
				catch
				{
				}
			});
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to load config from '" + ConfigPath + "', falling back to default configuration", ex);
			BackupCorruptConfig();
			IsFallbackConfig = true;
			CurrentConfig = CreateDefaultConfig();
			EnsureConfigHealth(CurrentConfig);
			I18n.SetLanguage(CurrentConfig.Language);
			MarkConfigurationChanged();
			EnsureConfigsFolder();
		}
	}

	public static void EnsureConfigHealth(AppConfig currentConfig)
	{
		if (currentConfig == null) return;
		if (string.IsNullOrWhiteSpace(currentConfig.ActiveConfigProfileName))
		{
			currentConfig.ActiveConfigProfileName = "默认配置";
		}
		if (currentConfig.BlacklistedProcesses == null)
		{
			currentConfig.BlacklistedProcesses = new List<string> { "mstsc.exe", "paint.exe" };
		}
		if (currentConfig.WhitelistedProcesses == null)
		{
			currentConfig.WhitelistedProcesses = new List<string>();
		}
		if (string.IsNullOrEmpty(currentConfig.IsolationMode))
		{
			currentConfig.IsolationMode = "Blacklist";
		}
		if (currentConfig.MouseReleaseDebounceMs <= 0)
		{
			currentConfig.MouseReleaseDebounceMs = 12;
		}
		currentConfig.MouseReleaseDebounceMs = Math.Clamp(currentConfig.MouseReleaseDebounceMs, 1, 100);
		currentConfig.Profiles ??= new List<WheelProfile>();
		if (currentConfig.Profiles.Count == 0)
		{
			currentConfig.Profiles.Add(new WheelProfile
			{
				ProcessName = "Global",
				SectorCount = 8,
				Actions = new List<ActionItem>()
			});
		}

		// 确保 Global 方案位于首位
		int globalIndex = currentConfig.Profiles.FindIndex((WheelProfile p) => string.Equals(p.ProcessName, "Global", StringComparison.OrdinalIgnoreCase));
		if (globalIndex < 0)
		{
			currentConfig.Profiles.Insert(0, new WheelProfile
			{
				ProcessName = "Global",
				SectorCount = 8,
				Actions = new List<ActionItem>()
			});
		}
		else if (globalIndex > 0)
		{
			var gp = currentConfig.Profiles[globalIndex];
			currentConfig.Profiles.RemoveAt(globalIndex);
			currentConfig.Profiles.Insert(0, gp);
		}

		foreach (WheelProfile profile in currentConfig.Profiles)
		{
			if (profile == null) continue;
			profile.EnsureLayers();
			if (profile.Actions != null)
			{
				foreach (ActionItem action in profile.Actions)
				{
					if (action != null && action.SubActions == null)
					{
						action.SubActions = new List<ActionItem>();
					}
				}
			}
			if (profile.Layers != null)
			{
				foreach (WheelLayer layer in profile.Layers)
				{
					if (layer.Actions != null)
					{
						foreach (ActionItem action in layer.Actions)
						{
							if (action != null && action.SubActions == null)
							{
								action.SubActions = new List<ActionItem>();
							}
						}
					}
				}
			}
			profile.SyncRootPropertiesFromActiveLayer();
		}

		// 自动自愈此前版本中因初始事件误改写的槽位动作（保留有效程序路径，但动作被误写为 Tile / 2L）
		foreach (WheelProfile profile in currentConfig.Profiles)
		{
			if (profile?.Actions == null) continue;
			foreach (ActionItem action in profile.Actions)
			{
				if (action == null) continue;
				if (action.Type == "Tile" && action.Parameter == "2L" &&
					!string.IsNullOrWhiteSpace(action.InheritAppIconPath) &&
					(action.InheritAppIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
					 action.InheritAppIconPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
					 action.InheritAppIconPath.Contains("\\") || action.InheritAppIconPath.Contains("/")) &&
					(action.Name != null && action.Name.Contains("平铺")))
				{
					action.Type = "Launch";
					action.Parameter = action.InheritAppIconPath;
					try
					{
						string baseName = Path.GetFileNameWithoutExtension(action.InheritAppIconPath);
						if (!string.IsNullOrWhiteSpace(baseName))
						{
							action.Name = baseName;
						}
					}
					catch { }
				}
			}
			profile.SyncActiveLayerFromRootProperties();
		}

		CleanLegacyActionBase64(currentConfig);
		EnsureTriggerHealth(currentConfig);
	}

	private static void CleanLegacyActionBase64(AppConfig? config)
	{
		if (config?.Profiles == null) return;
		foreach (var profile in config.Profiles)
		{
			if (profile == null) continue;
			CleanActionsBase64(profile.Actions);
			if (profile.Layers != null)
			{
				foreach (var layer in profile.Layers)
				{
					if (layer != null) CleanActionsBase64(layer.Actions);
				}
			}
		}
	}

	private static void CleanActionsBase64(List<ActionItem>? actions)
	{
		if (actions == null) return;
		foreach (var a in actions)
		{
			if (a == null) continue;
			if (!string.IsNullOrEmpty(a.CustomIconSvg) && (a.CustomIconSvg.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) || a.CustomIconSvg.Length > 80000))
			{
				a.CustomIconSvg = string.Empty;
			}
			if (!string.IsNullOrEmpty(a.InheritAppIconPath) && a.InheritAppIconPath.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
			{
				a.InheritAppIconPath = string.Empty;
			}
			if (a.SubActions != null)
			{
				CleanActionsBase64(a.SubActions);
			}
		}
	}

	// 返回是否保存成功，调用方据此决定提示文案，避免无条件宣称“已保存”。
	public static bool SaveConfig()
	{
		// 配置即使因磁盘故障保存失败，当前进程中的内存态也已经改变；
		// 先失效轮盘渲染缓存，确保下一次呼出展示最新状态。
		MarkConfigurationChanged();
		try
		{
			if (!Directory.Exists(AppDataFolder))
			{
				Directory.CreateDirectory(AppDataFolder);
			}
			if (CurrentConfig != null)
			{
				if (CurrentConfig.Profiles != null)
				{
					foreach (var p in CurrentConfig.Profiles)
					{
						p?.SyncRootPropertiesFromActiveLayer();
					}
				}
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true
			};
			string contents = JsonSerializer.Serialize(CurrentConfig, options);
			// 原子写：先落临时文件再替换。直接 WriteAllText 一旦中途被中断（退出/崩溃/断电）
			// 会留下截断的 config.json，下次启动即被判为损坏并回落默认配置。
			string tempPath = ConfigPath + ".tmp";
			File.WriteAllText(tempPath, contents);
			if (File.Exists(ConfigPath))
			{
				// 保留上一份完好配置，替换失败时仍可人工回退
				File.Replace(tempPath, ConfigPath, ConfigPath + ".bak");
			}
			else
			{
				File.Move(tempPath, ConfigPath, overwrite: true);
			}

			// 同步保存至方案子目录对应的配置文件
			try
			{
				string? activeProfile = CurrentConfig?.ActiveConfigProfileName;
				if (!string.IsNullOrWhiteSpace(activeProfile))
				{
					if (!Directory.Exists(ConfigsFolder))
					{
						Directory.CreateDirectory(ConfigsFolder);
					}
					string clean = CleanFileName(activeProfile);
					if (!string.IsNullOrWhiteSpace(clean))
					{
						string profilePath = Path.Combine(ConfigsFolder, $"{clean}.json");
						File.WriteAllText(profilePath, contents);
					}
				}
			}
			catch (Exception exSync)
			{
				AppLogger.LogError("Failed to mirror save profile config", exSync);
			}

			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to save config to file", ex);
			return false;
		}
	}

	public static WheelProfile GetProfileForProcess(string processName)
	{
		if (string.IsNullOrEmpty(processName))
		{
			return GetGlobalProfile();
		}
		string cleanProc = processName.Trim().ToLowerInvariant();
		string cleanBase = cleanProc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			? cleanProc.Substring(0, cleanProc.Length - 4)
			: cleanProc;

		if (CurrentConfig?.Profiles != null)
		{
			// 1. 优先在所有非 Global 的专属方案中匹配绑定的程序情景
			foreach (WheelProfile profile in CurrentConfig.Profiles)
			{
				if (profile == null || string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				// 检查 BoundProcesses 字段（支持逗号/分号/空格分隔多个进程）
				if (!string.IsNullOrWhiteSpace(profile.BoundProcesses))
				{
					string[] tokens = profile.BoundProcesses.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (string token in tokens)
					{
						string target = token.Trim().ToLowerInvariant();
						string targetBase = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
							? target.Substring(0, target.Length - 4)
							: target;

						if (target == cleanProc || targetBase == cleanBase)
						{
							return profile;
						}
					}
				}

				// 回退检查 ProcessName 字段
				string pProc = profile.ProcessName.Trim().ToLowerInvariant();
				string pBase = pProc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
					? pProc.Substring(0, pProc.Length - 4)
					: pProc;

				if (pProc == cleanProc || pBase == cleanBase)
				{
					return profile;
				}

				// 检查 DisplayName 字段（若用户将显示名称直接设为了目标程序名或进程名）
				if (!string.IsNullOrWhiteSpace(profile.DisplayName))
				{
					string dProc = profile.DisplayName.Trim().ToLowerInvariant();
					string dBase = dProc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
						? dProc.Substring(0, dProc.Length - 4)
						: dProc;

					if (dProc == cleanProc || dBase == cleanBase)
					{
						return profile;
					}
				}
			}
		}

		// 2. 无专属方案匹配时兜底回落至 Global
		return GetGlobalProfile();
	}

	public static WheelProfile GetGlobalProfile()
	{
		WheelProfile wheelProfile = CurrentConfig.Profiles.Find((WheelProfile p) => p.ProcessName.Equals("Global", StringComparison.OrdinalIgnoreCase));
		if (wheelProfile == null)
		{
			wheelProfile = new WheelProfile
			{
				ProcessName = "Global",
				SectorCount = 8,
				Actions = new List<ActionItem>()
			};
			CurrentConfig.Profiles.Insert(0, wheelProfile);
		}
		return wheelProfile;
	}

	public static AppConfig CreateDefaultConfig()
	{
		AppConfig obj = new AppConfig
		{
			DragThreshold = 25.0
		};
		WheelProfile item = new WheelProfile
		{
			ProcessName = "Global",
			SectorCount = 8,
			Actions = new List<ActionItem>
			{
				new ActionItem
				{
					Type = "Hotkey",
					Name = "复制 (Copy)",
					Parameter = "Ctrl+C",
					IconKey = "Copy",
					SubActions = new List<ActionItem>
					{
						new ActionItem { Type = "Hotkey", Name = "粘贴 (Paste)", Parameter = "Ctrl+V", IconKey = "Paste" },
						new ActionItem { Type = "Hotkey", Name = "剪切 (Cut)", Parameter = "Ctrl+X", IconKey = "Cut" },
						new ActionItem { Type = "Hotkey", Name = "全选 (Select All)", Parameter = "Ctrl+A", IconKey = "Folder" }
					}
				},
				new ActionItem
				{
					Type = "System",
					Name = "屏幕截图 (Capture)",
					Parameter = "Screenshot",
					IconKey = "Screenshot"
				},
				new ActionItem
				{
					Type = "System",
					Name = "显示桌面 (Desktop)",
					Parameter = "ShowDesktop",
					IconKey = "ShowDesktop",
					SubActions = new List<ActionItem>
					{
						new ActionItem { Type = "System", Name = "锁定电脑 (Lock)", Parameter = "Lock", IconKey = "Lock" }
					}
				},
				new ActionItem
				{
					Type = "System",
					Name = "多任务视图 (Task View)",
					Parameter = "TaskView",
					IconKey = "Camera"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "常用工具 (Utilities)",
					Parameter = "Ctrl+V",
					IconKey = "Paste",
					SubActions = new List<ActionItem>
					{
						new ActionItem { Type = "System", Name = "任务管理器", Parameter = "TaskManager", IconKey = "Terminal" },
						new ActionItem { Type = "System", Name = "计算器", Parameter = "Calculator", IconKey = "Code" },
						new ActionItem { Type = "Launch", Name = "记事本", Parameter = "notepad.exe", IconKey = "Code" },
						new ActionItem { Type = "System", Name = "控制面板", Parameter = "ControlPanel", IconKey = "Settings" }
					}
				},
				new ActionItem
				{
					Type = "System",
					Name = "音量减 (Vol Down)",
					Parameter = "VolumeDown",
					IconKey = "VolumeDown"
				},
				new ActionItem
				{
					Type = "Launch",
					Name = "浏览器 (Web Browser)",
					Parameter = "https://www.google.com",
					IconKey = "Browser",
					SubActions = new List<ActionItem>
					{
						new ActionItem { Type = "Launch", Name = "Google Chrome", Parameter = "chrome.exe", IconKey = "Chrome" },
						new ActionItem { Type = "Launch", Name = "Microsoft Edge", Parameter = "msedge.exe", IconKey = "Edge" },
						new ActionItem { Type = "Hotkey", Name = "新建标签页", Parameter = "Ctrl+T", IconKey = "NewTab" }
					}
				},
				new ActionItem
				{
					Type = "System",
					Name = "音量增 (Vol Up)",
					Parameter = "VolumeUp",
					IconKey = "VolumeUp"
				}
			}
		};
		WheelProfile item2 = new WheelProfile
		{
			ProcessName = "chrome.exe",
			SectorCount = 4,
			Actions = new List<ActionItem>
			{
				new ActionItem
				{
					Type = "Hotkey",
					Name = "关闭标签 (Close Tab)",
					Parameter = "Ctrl+W",
					IconKey = "CloseTab"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "后退 (Back)",
					Parameter = "Alt+Left",
					IconKey = "Back"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "新建标签 (New Tab)",
					Parameter = "Ctrl+T",
					IconKey = "NewTab"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "刷新 (Refresh)",
					Parameter = "F5",
					IconKey = "Refresh"
				}
			}
		};
		WheelProfile item3 = new WheelProfile
		{
			ProcessName = "code.exe",
			SectorCount = 8,
			Actions = new List<ActionItem>
			{
				new ActionItem
				{
					Type = "Hotkey",
					Name = "定义跳转 (F12)",
					Parameter = "F12",
					IconKey = "Code"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "格式化 (Format)",
					Parameter = "Shift+Alt+F",
					IconKey = "Edit"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "控制台 (Terminal)",
					Parameter = "Ctrl+`",
					IconKey = "Terminal"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "查找文件 (Quick Open)",
					Parameter = "Ctrl+P",
					IconKey = "Search"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "保存全部 (Save All)",
					Parameter = "Ctrl+K,S",
					IconKey = "Save"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "全局搜索 (Find in Files)",
					Parameter = "Ctrl+Shift+F",
					IconKey = "Search"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "撤销 (Undo)",
					Parameter = "Ctrl+Z",
					IconKey = "Undo"
				},
				new ActionItem
				{
					Type = "Hotkey",
					Name = "重做 (Redo)",
					Parameter = "Ctrl+Y",
					IconKey = "Redo"
				}
			}
		};
		obj.Profiles.Add(item);
		obj.Profiles.Add(item2);
		obj.Profiles.Add(item3);
		foreach (var p in obj.Profiles)
		{
			p.EnsureLayers();
		}
		return obj;
	}

	public static bool ExportConfig(string targetFilePath)
	{
		try
		{
			if (CurrentConfig != null)
			{
				if (CurrentConfig.Profiles != null)
				{
					foreach (var p in CurrentConfig.Profiles)
					{
						p?.SyncRootPropertiesFromActiveLayer();
					}
				}
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true
			};
			string contents = JsonSerializer.Serialize(CurrentConfig, options);
			File.WriteAllText(targetFilePath, contents);
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to export config to '" + targetFilePath + "'", ex);
			return false;
		}
	}

	public static bool ImportConfig(string sourceFilePath)
	{
		try
		{
			if (!File.Exists(sourceFilePath))
			{
				return false;
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			};
			AppConfig? appConfig = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(sourceFilePath), options);
			if (appConfig != null)
			{
				EnsureConfigHealth(appConfig);
				CurrentConfig = appConfig;
				I18n.SetLanguage(CurrentConfig.Language);
				SaveConfig();
				return true;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to import config from '" + sourceFilePath + "'", ex);
		}
		return false;
	}

	public static string ConfigsFolder => Path.Combine(AppDataFolder, "Configs");

	public static void EnsureConfigsFolder()
	{
		try
		{
			if (!Directory.Exists(ConfigsFolder))
			{
				Directory.CreateDirectory(ConfigsFolder);
			}

			string activeName = CurrentConfig?.ActiveConfigProfileName ?? "默认配置";
			if (string.IsNullOrWhiteSpace(activeName))
			{
				activeName = "默认配置";
				if (CurrentConfig != null)
				{
					CurrentConfig.ActiveConfigProfileName = activeName;
				}
			}

			string clean = CleanFileName(activeName);
			if (string.IsNullOrWhiteSpace(clean)) clean = "默认配置";

			string activeFile = Path.Combine(ConfigsFolder, $"{clean}.json");
			if (!File.Exists(activeFile))
			{
				if (CurrentConfig != null)
				{
					JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
					File.WriteAllText(activeFile, JsonSerializer.Serialize(CurrentConfig, options));
				}
				else if (File.Exists(ConfigPath))
				{
					File.Copy(ConfigPath, activeFile, overwrite: true);
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to ensure configs folder", ex);
		}
	}

	public static List<string> GetSavedConfigNames()
	{
		EnsureConfigsFolder();
		List<string> list = new List<string>();
		try
		{
			if (Directory.Exists(ConfigsFolder))
			{
				string[] files = Directory.GetFiles(ConfigsFolder, "*.json");
				foreach (string file in files)
				{
					string name = Path.GetFileNameWithoutExtension(file);
					if (!string.IsNullOrWhiteSpace(name))
					{
						list.Add(name);
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to list saved configs", ex);
		}

		if (list.Count == 0)
		{
			list.Add("默认配置");
		}
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	public static bool SaveConfigAs(string newProfileName)
	{
		if (string.IsNullOrWhiteSpace(newProfileName)) return false;
		string cleanName = CleanFileName(newProfileName);
		if (string.IsNullOrWhiteSpace(cleanName)) return false;

		EnsureConfigsFolder();
		if (CurrentConfig != null)
		{
			CurrentConfig.ActiveConfigProfileName = cleanName;
		}

		string targetFile = Path.Combine(ConfigsFolder, $"{cleanName}.json");
		try
		{
			JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
			string json = JsonSerializer.Serialize(CurrentConfig, options);
			File.WriteAllText(targetFile, json);
			SaveConfig();
			MarkConfigurationChanged();
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to save config as '{cleanName}'", ex);
			return false;
		}
	}

	public static bool SwitchToConfig(string profileName)
	{
		if (string.IsNullOrWhiteSpace(profileName)) return false;
		EnsureConfigsFolder();
		string cleanName = CleanFileName(profileName);
		string targetFile = Path.Combine(ConfigsFolder, $"{cleanName}.json");
		if (!File.Exists(targetFile)) return false;

		try
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			};
			AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(targetFile), options);
			if (loaded != null)
			{
				loaded.ActiveConfigProfileName = cleanName;
				EnsureConfigHealth(loaded);
				CurrentConfig = loaded;
				I18n.SetLanguage(CurrentConfig.Language);
				SaveConfig();
				MarkConfigurationChanged();
				return true;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to switch to config '{cleanName}'", ex);
		}
		return false;
	}

	public static bool RenameSavedConfig(string oldName, string newName)
	{
		if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
		string cleanOld = CleanFileName(oldName);
		string cleanNew = CleanFileName(newName);
		if (string.IsNullOrWhiteSpace(cleanOld) || string.IsNullOrWhiteSpace(cleanNew)) return false;
		if (string.Equals(cleanOld, cleanNew, StringComparison.OrdinalIgnoreCase)) return true;

		EnsureConfigsFolder();
		string oldFile = Path.Combine(ConfigsFolder, $"{cleanOld}.json");
		string newFile = Path.Combine(ConfigsFolder, $"{cleanNew}.json");
		if (!File.Exists(oldFile) || File.Exists(newFile)) return false;

		try
		{
			File.Move(oldFile, newFile);
			if (string.Equals(CurrentConfig?.ActiveConfigProfileName, cleanOld, StringComparison.OrdinalIgnoreCase))
			{
				CurrentConfig.ActiveConfigProfileName = cleanNew;
				SaveConfig();
			}
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to rename config '{cleanOld}' to '{cleanNew}'", ex);
			return false;
		}
	}

	public static bool DeleteSavedConfig(string profileName, out string fallbackName)
	{
		fallbackName = "";
		if (string.IsNullOrWhiteSpace(profileName)) return false;
		string clean = CleanFileName(profileName);

		EnsureConfigsFolder();
		List<string> all = GetSavedConfigNames();
		if (all.Count <= 1) return false;

		string file = Path.Combine(ConfigsFolder, $"{clean}.json");
		try
		{
			if (File.Exists(file))
			{
				File.Delete(file);
			}

			if (string.Equals(CurrentConfig?.ActiveConfigProfileName, clean, StringComparison.OrdinalIgnoreCase))
			{
				fallbackName = all.FirstOrDefault(x => !string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)) ?? "默认配置";
				SwitchToConfig(fallbackName);
			}
			else
			{
				fallbackName = CurrentConfig?.ActiveConfigProfileName ?? "默认配置";
			}
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to delete config '{clean}'", ex);
			return false;
		}
	}

	public static bool ImportExternalConfig(string externalFilePath, string? preferredName, out string importedName)
	{
		importedName = "";
		if (!File.Exists(externalFilePath)) return false;

		try
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			};
			AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(externalFilePath), options);
			if (loaded == null) return false;

			EnsureConfigsFolder();
			string baseName = !string.IsNullOrWhiteSpace(preferredName) ? preferredName : Path.GetFileNameWithoutExtension(externalFilePath);
			baseName = CleanFileName(baseName);
			if (string.IsNullOrWhiteSpace(baseName)) baseName = "导入方案";

			string targetName = baseName;
			int counter = 1;
			while (File.Exists(Path.Combine(ConfigsFolder, $"{targetName}.json")))
			{
				targetName = $"{baseName} ({counter++})";
			}

			loaded.ActiveConfigProfileName = targetName;
			EnsureConfigHealth(loaded);

			string targetFile = Path.Combine(ConfigsFolder, $"{targetName}.json");
			JsonSerializerOptions saveOpts = new JsonSerializerOptions { WriteIndented = true };
			File.WriteAllText(targetFile, JsonSerializer.Serialize(loaded, saveOpts));

			SwitchToConfig(targetName);
			importedName = targetName;
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to import external config '{externalFilePath}'", ex);
			return false;
		}
	}

	public static bool ExportConfigToFile(string profileName, string targetFilePath)
	{
		try
		{
			EnsureConfigsFolder();
			string clean = CleanFileName(profileName);
			string profileFile = Path.Combine(ConfigsFolder, $"{clean}.json");
			if (string.Equals(CurrentConfig?.ActiveConfigProfileName, clean, StringComparison.OrdinalIgnoreCase) || !File.Exists(profileFile))
			{
				return ExportConfig(targetFilePath);
			}

			File.Copy(profileFile, targetFilePath, overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to export config '{profileName}' to '{targetFilePath}'", ex);
			return false;
		}
	}

	public static string CleanFileName(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return "";
		char[] invalid = Path.GetInvalidFileNameChars();
		return new string(fileName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
	}

	public static void ResetToDefault(string? profileName = null)
	{
		string name = !string.IsNullOrWhiteSpace(profileName) ? profileName : (CurrentConfig?.ActiveConfigProfileName ?? "默认配置");
		string clean = CleanFileName(name);
		if (string.IsNullOrWhiteSpace(clean)) clean = "默认配置";

		AppConfig defaultConf = CreateDefaultConfig();
		defaultConf.ActiveConfigProfileName = clean;
		EnsureConfigHealth(defaultConf);
		CurrentConfig = defaultConf;
		I18n.SetLanguage(CurrentConfig.Language);
		SaveConfig();
		MarkConfigurationChanged();
	}

	private static void EnsureTriggerHealth(AppConfig? config)
	{
		if (config == null) return;
		if (config.Trigger != null &&
		    string.Equals(config.Trigger.MouseButton, "LeftButton", StringComparison.OrdinalIgnoreCase) &&
		    !config.Trigger.RequireCtrl && !config.Trigger.RequireShift &&
		    !config.Trigger.RequireAlt && !config.Trigger.RequireWin)
		{
			// 若配置了单独鼠标左键作为唤醒键，确保长按呼出开关开启，使长按可稳定唤醒轮盘，单机保持原生点击
			config.LongPressTrigger = true;
		}

		// 冲突防护：若开启了独立鼠标手势，且手势按键与主轮盘触发键冲突，自动调整手势按键为 MiddleButton（或避免同键硬拦截）
		string wheelBtn = config.Trigger?.MouseButton ?? config.TriggerButton ?? "RightButton";
		if (config.GestureEnabled && string.Equals(wheelBtn, config.GestureTriggerButton, StringComparison.OrdinalIgnoreCase))
		{
			config.GestureTriggerButton = string.Equals(wheelBtn, "MiddleButton", StringComparison.OrdinalIgnoreCase) ? "XButton1" : "MiddleButton";
		}
	}

	public static bool IsElevated()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			WindowsPrincipal principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	public static bool IsAutoStartEnabled()
	{
		if (IsRegistryAutoStartEnabled())
		{
			return true;
		}
		if (CurrentConfig != null && CurrentConfig.AutoStartAsAdmin)
		{
			return IsAdminTaskAutoStartEnabled();
		}
		return false;
	}

	public static bool IsRegistryAutoStartEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return (registryKey != null && registryKey.GetValue("StarPie") != null) || registryKey?.GetValue("WinPieGestures") != null;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsAdminTaskAutoStartEnabled()
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = "schtasks.exe",
				Arguments = "/query /tn \"StarPie_AdminAutoStart\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			});
			if (process == null)
			{
				return false;
			}
			process.WaitForExit(1500);
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	public static void SetAutoStart(bool enable, bool asAdmin = false)
	{
		try
		{
			string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StarPie.exe");
			if (enable)
			{
				// Always write registry auto-start as the foundational reliable guarantee
				SetRegistryAutoStart(exePath);

				if (asAdmin)
				{
					CreateOrUpdateAdminTask(exePath);
				}
				else
				{
					RemoveAdminTask();
				}
			}
			else
			{
				RemoveRegistryAutoStart();
				RemoveAdminTask();
			}
			if (CurrentConfig != null)
			{
				CurrentConfig.AutoStartAsAdmin = asAdmin;
				SaveConfig();
			}
		}
		catch (Exception)
		{
		}
	}

	private static void SetRegistryAutoStart(string exePath)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			registryKey.SetValue("StarPie", "\"" + exePath + "\" --autostart --minimized");
			try
			{
				registryKey.DeleteValue("WinPieGestures", throwOnMissingValue: false);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private static void RemoveRegistryAutoStart()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			try
			{
				registryKey.DeleteValue("StarPie", throwOnMissingValue: false);
			}
			catch
			{
			}
			try
			{
				registryKey.DeleteValue("WinPieGestures", throwOnMissingValue: false);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private static void CreateOrUpdateAdminTask(string exePath)
	{
		try
		{
			// /delay 0000:00 确保计划任务在用户登录后以零延迟（0秒）立即启动，消除 Windows 任务计划程序默认的数秒登录延迟
			string arguments = $"/create /tn \"StarPie_AdminAutoStart\" /tr \"\\\"{exePath}\\\" --autostart --minimized\" /sc onlogon /delay 0000:00 /rl highest /f";
			bool isElevated = IsElevated();
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "schtasks.exe",
				Arguments = arguments,
				UseShellExecute = !isElevated,
				Verb = isElevated ? "" : "runas",
				CreateNoWindow = isElevated,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			using Process process = Process.Start(psi);
			process?.WaitForExit(2000);
		}
		catch (Exception)
		{
		}
	}

	private static void RemoveAdminTask()
	{
		try
		{
			bool isElevated = IsElevated();
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "schtasks.exe",
				Arguments = "/delete /tn \"StarPie_AdminAutoStart\" /f",
				UseShellExecute = !isElevated,
				Verb = isElevated ? "" : "runas",
				CreateNoWindow = isElevated,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			using Process process = Process.Start(psi);
			process?.WaitForExit(2000);
		}
		catch
		{
		}
	}

	public static void EnsureAutoStartRegistryUpToDate()
	{
		try
		{
			// 若当前进程本身就是通过自启动参数呼起，说明任务与注册表均已正确就绪，直接跳过耗时的外置进程核验
			if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}

			if (CurrentConfig != null && CurrentConfig.AutoStartAsAdmin)
			{
				if (IsAdminTaskAutoStartEnabled())
				{
					CreateOrUpdateAdminTask(Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StarPie.exe"));
				}
				return;
			}
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			string text = (registryKey.GetValue("StarPie") as string) ?? (registryKey.GetValue("WinPieGestures") as string);
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			string text2 = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StarPie.exe");
			string text3 = "\"" + text2 + "\" --autostart --minimized";
			if (!(text != text3))
			{
				return;
			}
			registryKey.SetValue("StarPie", text3);
			try
			{
				registryKey.DeleteValue("WinPieGestures", throwOnMissingValue: false);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}
}
