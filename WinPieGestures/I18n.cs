using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinPieGestures;

public static class I18n
{
	private static LanguageCode _currentLanguage = LanguageCode.ZhCn;

	private static readonly Dictionary<string, Dictionary<LanguageCode, string>> Translations;

	public static LanguageCode CurrentLanguage
	{
		get
		{
			return _currentLanguage;
		}
		set
		{
			if (_currentLanguage != value)
			{
				_currentLanguage = value;
				LanguageChanged?.Invoke();
			}
		}
	}

	public static string CurrentLanguageCode => _currentLanguage switch
	{
		LanguageCode.ZhTw => "zh-TW", 
		LanguageCode.En => "en", 
		LanguageCode.Ja => "ja", 
		_ => "zh-CN", 
	};

	public static event Action? LanguageChanged;

	public static void SetLanguage(string code)
	{
		if (string.Equals(code, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			string name = CultureInfo.CurrentUICulture.Name;
			if (name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.ZhTw;
			}
			else if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.ZhCn;
			}
			else if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
			{
				CurrentLanguage = LanguageCode.Ja;
			}
			else
			{
				CurrentLanguage = LanguageCode.En;
			}
			return;
		}
		if (code != null)
		{
			int length = code.Length;
			if (length != 2)
			{
				if (length != 5)
				{
					if (length == 7 && code == "zh-Hant")
					{
						goto IL_0169;
					}
				}
				else
				{
					switch (code[3])
					{
					case 'T':
						break;
					case 'H':
						goto IL_0100;
					case 'U':
						goto IL_010f;
					case 'G':
						goto IL_011e;
					case 'J':
						goto IL_012d;
					default:
						goto IL_0175;
					}
					if (code == "zh-TW")
					{
						goto IL_0169;
					}
				}
			}
			else
			{
				char c = code[0];
				if (c != 'e')
				{
					if (c == 'j' && code == "ja")
					{
						goto IL_0171;
					}
				}
				else if (code == "en")
				{
					goto IL_016d;
				}
			}
		}
		goto IL_0175;
		IL_012d:
		if (code == "ja-JP")
		{
			goto IL_0171;
		}
		goto IL_0175;
		IL_0100:
		if (code == "zh-HK")
		{
			goto IL_0169;
		}
		goto IL_0175;
		IL_010f:
		if (code == "en-US")
		{
			goto IL_016d;
		}
		goto IL_0175;
		IL_0169:
		LanguageCode currentLanguage = LanguageCode.ZhTw;
		goto IL_0177;
		IL_0175:
		currentLanguage = LanguageCode.ZhCn;
		goto IL_0177;
		IL_0171:
		currentLanguage = LanguageCode.Ja;
		goto IL_0177;
		IL_0177:
		CurrentLanguage = currentLanguage;
		return;
		IL_016d:
		currentLanguage = LanguageCode.En;
		goto IL_0177;
		IL_011e:
		if (code == "en-GB")
		{
			goto IL_016d;
		}
		goto IL_0175;
	}

	public static string T(string key)
	{
		return GetString(key);
	}

	public static string GetString(string key)
	{
		if (Translations.TryGetValue(key, out Dictionary<LanguageCode, string> value))
		{
			if (value.TryGetValue(_currentLanguage, out var value2))
			{
				return value2;
			}
			if (value.TryGetValue(LanguageCode.ZhCn, out var value3))
			{
				return value3;
			}
		}
		return key;
	}

	static I18n()
	{
		Dictionary<string, Dictionary<LanguageCode, string>> dictionary = new Dictionary<string, Dictionary<LanguageCode, string>>();
		dictionary["SidebarModeSimple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡易模式",
			[LanguageCode.En] = "💡 Simple Mode",
			[LanguageCode.Ja] = "💡 シンプルモード"
		};
		dictionary["SidebarModePro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级全星模式",
			[LanguageCode.ZhTw] = "⚙️ 進階全星模式",
			[LanguageCode.En] = "⚙️ Pro Full Mode",
			[LanguageCode.Ja] = "⚙️ プロ全星モード"
		};
		dictionary["NewCustomPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建配色",
			[LanguageCode.ZhTw] = "➕ 新建配色",
			[LanguageCode.En] = "➕ New Theme",
			[LanguageCode.Ja] = "➕ 新規配色"
		};
		dictionary["NewCustomPresetTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "新建配色方案",
			[LanguageCode.ZhTw] = "新建配色方案",
			[LanguageCode.En] = "New Color Theme Preset",
			[LanguageCode.Ja] = "新しいカラーテーマ"
		};
		dictionary["NewCustomPresetPrompt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "请输入新配色方案名称：",
			[LanguageCode.ZhTw] = "請輸入新配色方案名稱：",
			[LanguageCode.En] = "Enter a name for the new color theme:",
			[LanguageCode.Ja] = "新しいカラーテーマ名を入力してください:"
		};
		dictionary["SavePresetChangesButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcbe 保存当前配色修改",
			[LanguageCode.ZhTw] = "\ud83d\udcbe 儲存當前配色修改",
			[LanguageCode.En] = "\ud83d\udcbe Save Color Changes",
			[LanguageCode.Ja] = "\ud83d\udcbe 現在の配色変更を保存"
		};
		dictionary["SaveAsNewPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 另存为新预设...",
			[LanguageCode.ZhTw] = "➕ 另存為新預設...",
			[LanguageCode.En] = "➕ Save as New Preset...",
			[LanguageCode.Ja] = "➕ 新規プリセットとして保存..."
		};
		dictionary["DeletePresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 删除预设",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 刪除預設",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Delete Preset",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f プリセットを削除"
		};
		dictionary["RenameCustomPresetButton"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏\ufe0f 重命名预设",
			[LanguageCode.ZhTw] = "✏\ufe0f 重新命名預設",
			[LanguageCode.En] = "✏\ufe0f Rename Preset",
			[LanguageCode.Ja] = "✏\ufe0f プリセット名を変更"
		};
		dictionary["RenameCustomPresetTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重命名配色方案预设",
			[LanguageCode.ZhTw] = "重新命名配色方案預設",
			[LanguageCode.En] = "Rename Color Preset",
			[LanguageCode.Ja] = "カラープリセット名を変更"
		};
		dictionary["RenameCustomPresetPrompt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "请输入配色方案预设的新名称：",
			[LanguageCode.ZhTw] = "請輸入配色方案預設的新名稱：",
			[LanguageCode.En] = "Enter a new name for the color preset:",
			[LanguageCode.Ja] = "カラープリセットの新しい名前を入力してください:"
		};
		dictionary["CustomColorsExpanderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune sector background, highlight glow, border outlines, text and arc colors.",
			[LanguageCode.Ja] = "展開してセクター背景、ハイライトグロー、ボーダー、テキスト色などを微調整できます。"
		};
		dictionary["WheelFontFamily"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘文字字体 (Font Family):",
			[LanguageCode.ZhTw] = "輪盤文字字體 (Font Family):",
			[LanguageCode.En] = "Wheel Font Family:",
			[LanguageCode.Ja] = "ホイールのフォント:"
		};
		
		dictionary["SubmenuStyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级菜单样式 (Submenu Style)",
			[LanguageCode.ZhTw] = "二級選單樣式 (Submenu Style)",
			[LanguageCode.En] = "Submenu Style",
			[LanguageCode.Ja] = "サブメニューのスタイル"
		};
		dictionary["SubmenuStyleDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外圈子环：子动作沿选中扇区外侧环形展开（最多4个）；蜂窝扇：子动作以选中项为中心呈扇形排列（最多3个）。",
			[LanguageCode.ZhTw] = "外圈子環：子動作沿選中扇區外側環形展開（最多4個）；蜂窩扇：子動作以選中項為中心呈扇形排列（最多3個）。",
			[LanguageCode.En] = "Sub-Ring: Sub-actions expand in an outer concentric ring (up to 4 items); Honeycomb Fan: Expands outward from the selected sector in a tight fan (up to 3 items).",
			[LanguageCode.Ja] = "外周リング：選択したセクターの外側にリング状に展開（最大4項目）；ハニカムファン：扇状にコンパクトに展開（最大3項目）。"
		};
		dictionary["SubmenuStyleWheel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 外圈子环 (Sub-Ring)",
			[LanguageCode.ZhTw] = "🌐 外圈子環 (Sub-Ring)",
			[LanguageCode.En] = "🌐 Outer Sub-Ring",
			[LanguageCode.Ja] = "🌐 外周同心リング"
		};
		dictionary["SubmenuStyleFan"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🍯 蜂窝扇 (Honeycomb Fan)",
			[LanguageCode.ZhTw] = "🍯 蜂窩扇 (Honeycomb Fan)",
			[LanguageCode.En] = "🍯 Honeycomb Fan",
			[LanguageCode.Ja] = "🍯 ハニカムファン"
		};

		dictionary["OuterEscapeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "顺势外甩脱离取消 (Outer Escape Cancel)",
			[LanguageCode.ZhTw] = "順勢外甩脫離取消 (Outer Escape Cancel)",
			[LanguageCode.En] = "Outer Escape Cancel",
			[LanguageCode.Ja] = "外側スワイプでキャンセル (Outer Escape)"
		};
		dictionary["OuterEscapeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势划出后若想放弃，无需拉回中心，直接顺势向外快速划出即可安全取消，0 误触。",
			[LanguageCode.ZhTw] = "手勢劃出後若想放棄，無需拉回中心，直接順勢向外快速劃出即可安全取消，0 誤觸。",
			[LanguageCode.En] = "Flick cursor outwards past the wheel radius to safely cancel without returning to center.",
			[LanguageCode.Ja] = "ホイールの外側へ素早くスワイプすることで、安全に操作をキャンセルできます。"
		};
		dictionary["OuterEscapeDistanceTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩取消距离灵敏度 (Escape Distance):",
			[LanguageCode.ZhTw] = "外甩取消距離靈敏度 (Escape Distance):",
			[LanguageCode.En] = "Escape Distance Threshold (Sensitivity):",
			[LanguageCode.Ja] = "キャンセルスワイプ距離 (感度設定):"
		};
		dictionary["OuterEscapeDistanceDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "设定光标划出距离中心多远时判定为放弃。数值越小越灵敏（更易甩出取消），数值越大越沉稳（需甩得更远）。",
			[LanguageCode.ZhTw] = "設定游標劃出距離中心多遠時判定為放棄。數值越小越靈敏（更易甩出取消），數值越大越沉穩（需甩得更遠）。",
			[LanguageCode.En] = "How far past the center the cursor must travel to cancel. Smaller values cancel easier, larger values require a farther flick.",
			[LanguageCode.Ja] = "中心からどれだけ離れたらキャンセルとするかを設定します。値が小さいほど敏感になり、大きいほど遠くへのスワイプが必要になります。"
		};
		dictionary["OuterEscapeCheckbox"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用向外顺势甩出取消手势 (推荐开启)",
			[LanguageCode.ZhTw] = "啟用向外順勢甩出取消手勢 (推薦開啟)",
			[LanguageCode.En] = "Enable Outer Escape Cancel (Recommended)",
			[LanguageCode.Ja] = "外側スワイプキャンセルを有効化 (推奨)"
		};
		dictionary["VolumeDragTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "音量拖距调音 (Volume Drag-Adjust)",
			[LanguageCode.ZhTw] = "音量拖距調音 (Volume Drag-Adjust)",
			[LanguageCode.En] = "Volume Drag-Adjust",
			[LanguageCode.Ja] = "音量ドラッグ調整 (Volume Drag-Adjust)"
		};
		dictionary["VolumeDragDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "长按音量加/减扇区向外拖出可连续调音，以下距离参数实时生效，无需重启。",
			[LanguageCode.ZhTw] = "長按音量加/減扇區向外拖出可連續調音，以下距離參數即時生效，無需重啟。",
			[LanguageCode.En] = "Drag outward from a volume +/- sector to adjust continuously; the distance options below apply live without restart.",
			[LanguageCode.Ja] = "音量+/-セクターから外側へドラッグして連続調整。以下の距離パラメータは再起動不要で即時反映されます。"
		};
		dictionary["VolumeCancelRatioTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "缩回取消系数 (Cancel Hysteresis):",
			[LanguageCode.ZhTw] = "縮回取消係數 (Cancel Hysteresis):",
			[LanguageCode.En] = "Return-to-center cancel ratio (Hysteresis):",
			[LanguageCode.Ja] = "中心復帰キャンセル係数 (Hysteresis):"
		};
		dictionary["VolumeCancelRatioDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调音时缩回到触发距离的百分之多少以下即取消并恢复原音量。值越大越不易被边缘抖动误回收（默认 60%）。",
			[LanguageCode.ZhTw] = "調音時縮回到觸發距離的百分之多少以下即取消並恢復原音量。值越大越不易被邊緣抖動誤回收（預設 60%）。",
			[LanguageCode.En] = "Cancel and restore the original volume once you drag back below this share of the trigger distance. Higher resists edge jitter (default 60%).",
			[LanguageCode.Ja] = "トリガー距離のこの割合まで戻すとキャンセルして元の音量に戻します。値が大きいほど端の揺れによる誤収斂ににくくなります（既定60%）。"
		};
		dictionary["VolumeFlickFarTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "甩出取消距离下限 (Flick Far):",
			[LanguageCode.ZhTw] = "甩出取消距離下限 (Flick Far):",
			[LanguageCode.En] = "Flick-out cancel distance floor (Flick Far):",
			[LanguageCode.Ja] = "スワイプキャンセル距離下限 (Flick Far):"
		};
		dictionary["VolumeFlickFarDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅当光标已远于该距离时，快速甩动才会取消调音，防止近距离拖动被误判为甩出。",
			[LanguageCode.ZhTw] = "僅當光標已遠於該距離時，快速甩動才會取消調音，防止近距離拖動被誤判為甩出。",
			[LanguageCode.En] = "A fast flick only cancels once the pointer is beyond this distance, avoiding short drags being read as a flick.",
			[LanguageCode.Ja] = "カーソルがこの距離を超えて初めて高速スワイプでキャンセル。近距離ドラッグの誤判定を防ぎます。"
		};
		dictionary["VolumeFlickJumpTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "甩动跳变阈值 (Flick Jump):",
			[LanguageCode.ZhTw] = "甩動跳變閾值 (Flick Jump):",
			[LanguageCode.En] = "Per-frame flick jump threshold (Flick Jump):",
			[LanguageCode.Ja] = "フレームあたりスワイプ跳変閾値 (Flick Jump):"
		};
		dictionary["VolumeFlickJumpDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "一帧之内光标移动超过该距离才算快速甩动。值越大越不容易触发甩出取消。",
			[LanguageCode.ZhTw] = "一幀之內光標移動超過該距離才算快速甩動。值越大越不容易觸發甩出取消。",
			[LanguageCode.En] = "Movement beyond this distance within one frame counts as a fast flick. Larger values make flick-cancel less likely.",
			[LanguageCode.Ja] = "1フレーム内の移動がこの距離を超えると高速スワイプと判定。値が大きいほどスワイプキャンセルは起きにくくなります。"
		};
		dictionary["IconPickerImport"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 导入自定义图标...",
			[LanguageCode.ZhTw] = "➕ 匯入自訂圖示...",
			[LanguageCode.En] = "➕ Import Custom Icon...",
			[LanguageCode.Ja] = "➕ カスタムアイコンをインポート..."
		};
		dictionary["CustomColorsExpanderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 自定义高级配色与色彩微调",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 自訂進階配色與色彩微調",
			[LanguageCode.En] = "\ud83c\udfa8 Custom Advanced Color Tuning",
			[LanguageCode.Ja] = "\ud83c\udfa8 高度なカラーカスタマイズ"
		};
		dictionary["AnimSpeedTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "功能区高亮与过渡动效速度",
			[LanguageCode.ZhTw] = "功能區高亮與過渡動效速度",
			[LanguageCode.En] = "Hover & Transition Animation Speed",
			[LanguageCode.Ja] = "ホバー・遷移アニメーション速度"
		};
		dictionary["AnimSpeedDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节鼠标划向不同功能扇区时的高亮弹出与平滑过渡动画响应速度，定制专属跟手体验。",
			[LanguageCode.ZhTw] = "調節滑鼠滑向不同功能扇區時的高亮彈出與平滑過渡動畫響應速度，定制專屬手感。",
			[LanguageCode.En] = "Adjust the response animation speed when hovering and transitioning across sectors.",
			[LanguageCode.Ja] = "セクター間をホバー・移動する際のアニメーション速度を調整します。"
		};
		dictionary["AnimSpeedElegant"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf38 优雅 (130ms / 柔和细腻)",
			[LanguageCode.ZhTw] = "\ud83c\udf38 優雅 (130ms / 柔和細膩)",
			[LanguageCode.En] = "\ud83c\udf38 Elegant (130ms / Smooth & Soft)",
			[LanguageCode.Ja] = "\ud83c\udf38 エレガント (130ms / 滑らか)"
		};
		dictionary["AnimSpeedBalanced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 流畅 (80ms / 推荐默认)",
			[LanguageCode.ZhTw] = "⚡ 流暢 (80ms / 推薦預設)",
			[LanguageCode.En] = "⚡ Fluent (80ms / Recommended)",
			[LanguageCode.Ja] = "⚡ スムーズ (80ms / 推奨)"
		};
		dictionary["AnimSpeedFast"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\ude80 快速 (35ms / 极速响应)",
			[LanguageCode.ZhTw] = "\ud83d\ude80 快速 (35ms / 極速響應)",
			[LanguageCode.En] = "\ud83d\ude80 Snappy (35ms / Ultra-Fast)",
			[LanguageCode.Ja] = "\ud83d\ude80 高速 (35ms / 即座に応答)"
		};
		dictionary["CoreTransformSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图案与贴图尺寸及显示位置微调",
			[LanguageCode.ZhTw] = "圖案與貼圖尺寸及顯示位置微調",
			[LanguageCode.En] = "Core Pattern & Image Size and Position Tuning",
			[LanguageCode.Ja] = "コアパターン・画像のサイズと位置の微調整"
		};
		dictionary["CoreIconScaleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心图案大小缩放 (Scale / Zoom):",
			[LanguageCode.ZhTw] = "中心圖案大小縮放 (Scale / Zoom):",
			[LanguageCode.En] = "Core Pattern / Image Scale (Zoom):",
			[LanguageCode.Ja] = "中央パターン／画像のスケーリング (ズーム):"
		};
		dictionary["CoreImageOffsetXTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "水平显示位置偏移 (Offset X):",
			[LanguageCode.ZhTw] = "水平顯示位置偏移 (Offset X):",
			[LanguageCode.En] = "Horizontal Position Offset (X):",
			[LanguageCode.Ja] = "水平表示位置オフセット (X):"
		};
		dictionary["CoreImageOffsetYTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直显示位置偏移 (Offset Y):",
			[LanguageCode.ZhTw] = "垂直顯示位置偏移 (Offset Y):",
			[LanguageCode.En] = "Vertical Position Offset (Y):",
			[LanguageCode.Ja] = "垂直表示位置オフセット (Y):"
		};
		dictionary["BtnResetCoreTransform"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd04 重置图案大小与居中位置",
			[LanguageCode.ZhTw] = "\ud83d\udd04 重設圖案大小與置中位置",
			[LanguageCode.En] = "\ud83d\udd04 Reset Size & Center Position",
			[LanguageCode.Ja] = "\ud83d\udd04 サイズと中央位置をリセット"
		};
		dictionary["CoreImagePerformanceTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示：推荐使用 256×256 ~ 512×512 适中分辨率的图片或 SVG 矢量图。导入超高分辨率（如 4K/8K 原图）会增加 GPU 内存占用与重采样计算开销，可能影响手势呼出与高刷响应性能。",
			[LanguageCode.ZhTw] = "提示：建議使用 256×256 ~ 512×512 適中解析度的圖片或 SVG 向量圖。匯入超高解析度（如 4K/8K 原圖）會增加 GPU 記憶體佔用與重採樣計算開銷，可能影響手勢呼出與高刷響應效能。",
			[LanguageCode.En] = "Tip: Recommended image size is 256×256 ~ 512×512 px or SVG vectors. Importing ultra-high resolution images (e.g. 4K/8K) increases GPU memory and texture sampling overhead, which may impact gesture responsiveness.",
			[LanguageCode.Ja] = "ヒント: 256×256～512×512 px の画像または SVG ベクター画像の使用を推奨します。超高解像度画像（4K/8K など）を使用すると、GPU メモリ使用量と再サンプリング負荷が増加し、応答性に影響を与える場合があります。"
		};
		dictionary["EnableMultiTier"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用多级轮盘与级联子菜单 (Multi-Tier Sub-Wheels)",
			[LanguageCode.ZhTw] = "啟用多級輪盤與級聯子選單 (Multi-Tier Sub-Wheels)",
			[LanguageCode.En] = "Enable Multi-Tier Cascading Sub-Wheels",
			[LanguageCode.Ja] = "マルチ階層サブホイール機能を有効化"
		};
		dictionary["EnableMultiTierDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启后，若扇区配置了二级子动作，光标悬停时外圈将平滑展开扇形级联子菜单，向外划动即可精准触发子功能。",
			[LanguageCode.ZhTw] = "開啟後，若扇區配置了二級子動作，游標懸停時外圈將平滑展開扇形級聯子選單，向外劃動即可精準觸發子功能。",
			[LanguageCode.En] = "When enabled, hovering over a sector with sub-actions will smoothly expand cascading outer sub-sectors. Flick outward to trigger.",
			[LanguageCode.Ja] = "有効にすると、サブアクションが設定されたセクターにホバーした際に外側にカスケードサブメニューが展開され、外側へスワイプしてトリガーできます。"
		};
		dictionary["AutoExpandSubRingsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "唤出时直接同时展开一二级轮盘 (Auto-Expand Sub-Rings)",
			[LanguageCode.ZhTw] = "喚出時直接同時展開一二級輪盤 (Auto-Expand Sub-Rings)",
			[LanguageCode.En] = "Expand Sub-Rings Simultaneously on Popup",
			[LanguageCode.Ja] = "ポップアップ時にサブリングを同時展開"
		};
		dictionary["AutoExpandSubRingsDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开启后，呼出轮盘时所有已配置二级动作的外圈子环将与一级扇区同时呈现，无需向外划出触发距离即可一览全部操作。",
			[LanguageCode.ZhTw] = "開啟後，呼出輪盤時所有已配置二級動作的外圈子環將與一級扇區同時呈現，無需向外劃出觸發距離即可一覽全部操作。",
			[LanguageCode.En] = "When enabled, outer sub-rings for all configured sectors expand simultaneously upon popup without needing to drag past trigger distance.",
			[LanguageCode.Ja] = "有効にすると、ポップアップ時にトリガー距離をスワイプしなくても、設定済みのすべてのサブリングがメインセクターと同時に展開されます。"
		};
		dictionary["IsolationModeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程隔离与生效模式 (Process Isolation Mode)",
			[LanguageCode.ZhTw] = "處理程序隔離與生效模式 (Process Isolation Mode)",
			[LanguageCode.En] = "Process Isolation & Activation Mode",
			[LanguageCode.Ja] = "プロセス分離と有効化モード"
		};
		dictionary["IsolationBlacklistRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udeab 排除黑名单模式 (默认：全局生效，仅在黑名单程序中放行右键)",
			[LanguageCode.ZhTw] = "\ud83d\udeab 排除黑名單模式 (預設：全域生效，僅在黑名單程式中放行右鍵)",
			[LanguageCode.En] = "\ud83d\udeab Blacklist Mode (Global active, bypass in blacklisted apps)",
			[LanguageCode.Ja] = "\ud83d\udeab ブラックリストモード (既定: 全体有効、除外アプリのみ右クリック通過)"
		};
		dictionary["IsolationWhitelistRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 启用白名单模式 (仅在白名单程序中生效，其余程序完全放行右键)",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 啟用白名單模式 (僅在白名單程式中生效，其餘程式完全放行右鍵)",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Whitelist Mode (Only active in whitelisted apps, bypass elsewhere)",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f ホワイトリストモード (登録アプリのみ有効、他は右クリック通過)"
		};
		dictionary["BlacklistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程排除黑名单",
			[LanguageCode.ZhTw] = "處理程序排除黑名單",
			[LanguageCode.En] = "Process Exclusion Blacklist",
			[LanguageCode.Ja] = "プロセス除外ブラックリスト"
		};
		dictionary["BlacklistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在排除黑名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "在排除黑名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Bypass mouse gestures in blacklisted applications (e.g. Remote Desktop, Paint, CAD tools).",
			[LanguageCode.Ja] = "ブラックリストに登録されたアプリ（リモートデスクトップ、ペイントなど）ではジェスチャーを無効化します。"
		};
		dictionary["WhitelistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程启用白名单",
			[LanguageCode.ZhTw] = "處理程序啟用白名單",
			[LanguageCode.En] = "Process Activation Whitelist",
			[LanguageCode.Ja] = "プロセス有効化ホワイトリスト"
		};
		dictionary["WhitelistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势轮盘仅在白名单列表中的应用程序中生效，其他所有程序完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "手勢輪盤僅在白名單列表中的應用程式中生效，其他所有程式完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Mouse gestures will ONLY activate in whitelisted applications, bypassing everywhere else.",
			[LanguageCode.Ja] = "ホワイトリストに登録されたアプリのみでジェスチャーが有効になり、他のアプリでは通過します。"
		};
		dictionary["SubActionColumnHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "级联子菜单",
			[LanguageCode.ZhTw] = "級聯子選單",
			[LanguageCode.En] = "Sub-Menu",
			[LanguageCode.Ja] = "サブメニュー"
		};
		dictionary["CustomColorsExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开后可精准微调扇区底色、高亮光晕、边框线条、文字与光弧等各项色彩。",
			[LanguageCode.ZhTw] = "展開後可精準微調扇區底色、高亮光暈、邊框線條、文字與光弧等各項色彩。",
			[LanguageCode.En] = "Expand to fine-tune individual colors for sectors, highlights, borders, text, and glow.",
			[LanguageCode.Ja] = "セクター、ハイライト、ボーダー、テキストなどの色を個別に調整します。"
		};
		dictionary["MilestonesOlderExpander"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcdc 展开查看更早的历史版本演进 (Older Milestones)",
			[LanguageCode.ZhTw] = "\ud83d\udcdc 展開查看更早的歷史版本演進 (Older Milestones)",
			[LanguageCode.En] = "\ud83d\udcdc View Older Milestones",
			[LanguageCode.Ja] = "\ud83d\udcdc 過去の更新履歴を表示"
		};
		dictionary["BrowseAppTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择应用程序或快捷方式...",
			[LanguageCode.ZhTw] = "選擇應用程式或捷徑...",
			[LanguageCode.En] = "Browse application or shortcut...",
			[LanguageCode.Ja] = "アプリまたはショートカットを参照..."
		};
		dictionary["BrowseFolderTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择本地文件夹...",
			[LanguageCode.ZhTw] = "選擇本機資料夾...",
			[LanguageCode.En] = "Browse local folder...",
			[LanguageCode.Ja] = "フォルダーを参照..."
		};
		dictionary["BtnConfirm"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定",
			[LanguageCode.ZhTw] = "確定",
			[LanguageCode.En] = "Confirm",
			[LanguageCode.Ja] = "確定"
		};
		dictionary["BtnCancel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "取消",
			[LanguageCode.ZhTw] = "取消",
			[LanguageCode.En] = "Cancel",
			[LanguageCode.Ja] = "キャンセル"
		};
		dictionary["BtnOk"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定",
			[LanguageCode.ZhTw] = "確定",
			[LanguageCode.En] = "OK",
			[LanguageCode.Ja] = "OK"
		};
		dictionary["BtnApply"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "应用",
			[LanguageCode.ZhTw] = "套用",
			[LanguageCode.En] = "Apply",
			[LanguageCode.Ja] = "適用"
		};
		dictionary["BtnTest"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "测试",
			[LanguageCode.ZhTw] = "測試",
			[LanguageCode.En] = "Test",
			[LanguageCode.Ja] = "テスト"
		};
		dictionary["BtnBrowseFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择文件夹...",
			[LanguageCode.ZhTw] = "選擇資料夾...",
			[LanguageCode.En] = "Browse Folder...",
			[LanguageCode.Ja] = "フォルダーを選択..."
		};
		dictionary["ActionTypeFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc2 打开文件夹",
			[LanguageCode.ZhTw] = "\ud83d\udcc2 開啟資料夾",
			[LanguageCode.En] = "\ud83d\udcc2 Open Folder",
			[LanguageCode.Ja] = "\ud83d\udcc2 フォルダーを開く"
		};
		dictionary["ActionTypeHotkeyShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷热键",
			[LanguageCode.ZhTw] = "快捷熱鍵",
			[LanguageCode.En] = "Hotkey",
			[LanguageCode.Ja] = "ショートカット"
		};
		dictionary["ActionTypeOcrShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "截屏识字 (OCR)",
			[LanguageCode.ZhTw] = "截圖識字 (OCR)",
			[LanguageCode.En] = "Screen OCR",
			[LanguageCode.Ja] = "画面OCR"
		};
		dictionary["ActionTypeLaunchShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启动程序",
			[LanguageCode.ZhTw] = "啟動程式",
			[LanguageCode.En] = "Run App",
			[LanguageCode.Ja] = "アプリ起動"
		};
		dictionary["ActionTypeFolderShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开文件夹",
			[LanguageCode.ZhTw] = "開啟資料夾",
			[LanguageCode.En] = "Open Folder",
			[LanguageCode.Ja] = "フォルダー"
		};
		dictionary["ActionTypeWebUrlShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "打开网址",
			[LanguageCode.ZhTw] = "開啟網址",
			[LanguageCode.En] = "Open URL",
			[LanguageCode.Ja] = "URLを開く"
		};
		dictionary["ActionTypeSystemShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统控制",
			[LanguageCode.ZhTw] = "系統控制",
			[LanguageCode.En] = "System",
			[LanguageCode.Ja] = "システム"
		};
		dictionary["ActionTypeShellToolShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统与右键工具",
			[LanguageCode.ZhTw] = "系統與右鍵工具",
			[LanguageCode.En] = "Shell & System Tools",
			[LanguageCode.Ja] = "シェル・右クリックツール"
		};
		dictionary["ActionTypeCommandShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "运行命令",
			[LanguageCode.ZhTw] = "執行命令",
			[LanguageCode.En] = "Run Command",
			[LanguageCode.Ja] = "コマンド実行"
		};
		dictionary["ActionTypeSwitchWindowShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换窗口",
			[LanguageCode.ZhTw] = "切換視窗",
			[LanguageCode.En] = "Switch Window",
			[LanguageCode.Ja] = "ウィンドウ切替"
		};
		dictionary["TerminalCmd"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "CMD",
			[LanguageCode.ZhTw] = "CMD",
			[LanguageCode.En] = "CMD",
			[LanguageCode.Ja] = "CMD"
		};
		dictionary["TerminalPowerShell"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Powershell",
			[LanguageCode.ZhTw] = "Powershell",
			[LanguageCode.En] = "Powershell",
			[LanguageCode.Ja] = "Powershell"
		};
		dictionary["TerminalWsl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "WSL",
			[LanguageCode.ZhTw] = "WSL",
			[LanguageCode.En] = "WSL",
			[LanguageCode.Ja] = "WSL"
		};
		dictionary["TerminalCmdHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "CMD (无终端)",
			[LanguageCode.ZhTw] = "CMD (無終端)",
			[LanguageCode.En] = "CMD (no window)",
			[LanguageCode.Ja] = "CMD (非表示)"
		};
		dictionary["TerminalPowerShellHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Powershell (无终端)",
			[LanguageCode.ZhTw] = "Powershell (無終端)",
			[LanguageCode.En] = "Powershell (no window)",
			[LanguageCode.Ja] = "Powershell (非表示)"
		};
		dictionary["TerminalWslHidden"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "WSL (无终端)",
			[LanguageCode.ZhTw] = "WSL (無終端)",
			[LanguageCode.En] = "WSL (no window)",
			[LanguageCode.Ja] = "WSL (非表示)"
		};
		dictionary["ProfileCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前配置方案 (Profile)",
			[LanguageCode.ZhTw] = "當前配置方案 (Profile)",
			[LanguageCode.En] = "Active Profiles",
			[LanguageCode.Ja] = "プロファイル設定"
		};
		dictionary["ProfileCardDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择或新建针对特定程序（如 Chrome、VS Code）或特定工作流的轮盘配置方案（支持双击重命名）。",
			[LanguageCode.ZhTw] = "選擇或新建針對特定程式（如 Chrome、VS Code）或特定工作流程的輪盤配置方案（支援按兩下重新命名）。",
			[LanguageCode.En] = "Select or create dedicated pie wheel profiles for specific apps (e.g. Chrome, VS Code) or workflows (double-click to rename).",
			[LanguageCode.Ja] = "アプリ（Chrome、VS Codeなど）やワークフローごとに専用のプロファイルを設定します（ダブルクリックで名前変更）。"
		};
		dictionary["BtnAddAppProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新增程序专属配置",
			[LanguageCode.ZhTw] = "➕ 新增程式專屬配置",
			[LanguageCode.En] = "➕ Add App Profile",
			[LanguageCode.Ja] = "➕ アプリ専用設定を追加"
		};
		dictionary["BtnAddCustomProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 新建自定义配置",
			[LanguageCode.ZhTw] = "➕ 新建自訂配置",
			[LanguageCode.En] = "➕ Add Custom Profile",
			[LanguageCode.Ja] = "➕ カスタム設定を追加"
		};
		dictionary["BtnRenameProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✏\ufe0f 重命名当前配置",
			[LanguageCode.ZhTw] = "✏\ufe0f 重新命名當前配置",
			[LanguageCode.En] = "✏\ufe0f Rename Profile",
			[LanguageCode.Ja] = "✏\ufe0f 名前を変更"
		};
		dictionary["BtnDeleteProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 删除当前配置",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 刪除當前配置",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Delete Profile",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f 設定を削除"
		};
		dictionary["BtnDuplicateProfile"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "📑 复制方案",
			[LanguageCode.ZhTw] = "📑 複製方案",
			[LanguageCode.En] = "📑 Duplicate Profile",
			[LanguageCode.Ja] = "📑 設定を複製"
		};
		dictionary["SectorCountOptionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区方位数量 (Sector Count)",
			[LanguageCode.ZhTw] = "扇區方位數量 (Sector Count)",
			[LanguageCode.En] = "Sector Count",
			[LanguageCode.Ja] = "セクター数（キー数）"
		};
		dictionary["SectorCountOptionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切换手势轮盘的切分数量。4 键最快最不易误触，8 键为标准全能方位，12 键适合功能密集场景。",
			[LanguageCode.ZhTw] = "切換手勢輪盤的切分數量。4 鍵最快最不易誤觸，8 鍵為標準全能方位，12 鍵適合功能密集場景。",
			[LanguageCode.En] = "Switch sector counts: 4-way for fast blind flicks, 8-way for balanced productivity, 12-way for high-density actions.",
			[LanguageCode.Ja] = "セクター数を切り替えます。4キー（誤操作防止）、8キー（標準全方位）、12キー（高密度機能）。"
		};
		dictionary["SectorActionListTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区动作映射列表",
			[LanguageCode.ZhTw] = "扇區動作對應列表",
			[LanguageCode.En] = "Sector Action Mappings",
			[LanguageCode.Ja] = "セクターアクションマッピング"
		};
		dictionary["SectorActionListDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为每个方位指定触发动作与图标。支持热键组合（如 Ctrl+C）、启动本地程序、打开文件夹与系统级操作。",
			[LanguageCode.ZhTw] = "為每個方位指定觸發動作與圖示。支援快捷熱鍵組合（如 Ctrl+C）、啟動本地程式、開啟資料夾與系統級操作。",
			[LanguageCode.En] = "Assign actions and icons for each sector. Supports hotkeys (e.g. Ctrl+C), app launching, folder opening, and system actions.",
			[LanguageCode.Ja] = "各方向の動作とアイコンを設定します。ショートカット（Ctrl+C等）、アプリ起動、フォルダー、システム制御に対応。"
		};
		dictionary["SectorActionListReorderHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击右侧功能卡的 ▲ / ▼ 箭头，将功能移动到相邻的轮盘位置槽。",
			[LanguageCode.ZhTw] = "點擊右側功能卡的 ▲ / ▼ 箭頭，將功能移動到相鄰的輪盤位置槽。",
			[LanguageCode.En] = "Click the ▲ / ▼ arrows on the right to move an action to an adjacent wheel-position slot.",
			[LanguageCode.Ja] = "右側の▲ / ▼ボタンをクリックして、アクションを隣のホイール位置へ移動します。"
		};
		dictionary["SectorPositionSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘位置槽",
			[LanguageCode.ZhTw] = "輪盤位置槽",
			[LanguageCode.En] = "Wheel position",
			[LanguageCode.Ja] = "ホイール位置"
		};
		dictionary["SectorMoveUp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将此功能上移一个轮盘位置",
			[LanguageCode.ZhTw] = "將此功能上移一個輪盤位置",
			[LanguageCode.En] = "Move this action up one wheel position",
			[LanguageCode.Ja] = "このアクションを1つ上のホイール位置へ移動"
		};
		dictionary["SectorMoveDown"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "将此功能下移一个轮盘位置",
			[LanguageCode.ZhTw] = "將此功能下移一個輪盤位置",
			[LanguageCode.En] = "Move this action down one wheel position",
			[LanguageCode.Ja] = "このアクションを1つ下のホイール位置へ移動"
		};
		dictionary["IconPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择动作矢量图标",
			[LanguageCode.ZhTw] = "選擇動作向量圖示",
			[LanguageCode.En] = "Select Vector Icon",
			[LanguageCode.Ja] = "ベクターアイコンを選択"
		};
		dictionary["IconPickerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择扇区动作矢量图标",
			[LanguageCode.ZhTw] = "選擇扇區動作向量圖示",
			[LanguageCode.En] = "Select Sector Vector Icon",
			[LanguageCode.Ja] = "セクターアイコンを選択"
		};
		dictionary["IconPickerSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "精选 30+ 常用高保真矢量图形，支持在不同分辨率及 DPI 下无损清晰渲染。",
			[LanguageCode.ZhTw] = "精選 30+ 常用高保真向量圖形，支援在不同解析度及 DPI 下無損清晰渲染。",
			[LanguageCode.En] = "30+ high-fidelity vector icons with lossless crisp rendering across all DPI displays.",
			[LanguageCode.Ja] = "30種類以上の高精細ベクターアイコン。あらゆるDPIで美しく描画されます。"
		};
		dictionary["IconPickerSearchTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "输入图标名称或分类进行快速过滤...",
			[LanguageCode.ZhTw] = "輸入圖示名稱或分類進行快速篩選...",
			[LanguageCode.En] = "Search icon name or category...",
			[LanguageCode.Ja] = "アイコン名またはカテゴリで検索..."
		};
		dictionary["IconPickerClear"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "清空图标 (无图标)",
			[LanguageCode.ZhTw] = "清空圖示 (無圖示)",
			[LanguageCode.En] = "Clear Icon (No Icon)",
			[LanguageCode.Ja] = "アイコンをクリア (なし)"
		};
		dictionary["IconPickerSelected"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已选图标:",
			[LanguageCode.ZhTw] = "已選圖示:",
			[LanguageCode.En] = "Selected Icon:",
			[LanguageCode.Ja] = "選択中のアイコン:"
		};
		dictionary["IconPickerNone"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "(未选择)",
			[LanguageCode.ZhTw] = "(未選擇)",
			[LanguageCode.En] = "(None)",
			[LanguageCode.Ja] = "(未選択)"
		};
		dictionary["ColorPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "色彩选择器与屏幕吸管 (Color Picker)",
			[LanguageCode.ZhTw] = "色彩選擇器與螢幕吸管 (Color Picker)",
			[LanguageCode.En] = "Color Picker & Eyedropper",
			[LanguageCode.Ja] = "カラーピッカー＆スポイト"
		};
		dictionary["ColorPickerHue"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "色相",
			[LanguageCode.ZhTw] = "色相",
			[LanguageCode.En] = "Hue",
			[LanguageCode.Ja] = "色相"
		};
		dictionary["ColorPickerAlpha"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "不透明",
			[LanguageCode.ZhTw] = "不透明",
			[LanguageCode.En] = "Opacity",
			[LanguageCode.Ja] = "不透明度"
		};
		dictionary["ColorPickerEyedropperTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 屏幕取色吸管",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 螢幕取色吸管",
			[LanguageCode.En] = "\ud83d\udd0d Screen Eyedropper",
			[LanguageCode.Ja] = "\ud83d\udd0d 画面スポイト"
		};
		dictionary["ColorPickerEyedropperDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击后在屏幕任意窗口吸取精准色彩",
			[LanguageCode.ZhTw] = "點擊後在螢幕任意視窗吸取精準色彩",
			[LanguageCode.En] = "Pick color accurately from any window or desktop on screen",
			[LanguageCode.Ja] = "画面上の任意のウィンドウから正確な色を抽出します"
		};
		dictionary["ColorPickerEyedropperBtn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从屏幕吸色",
			[LanguageCode.ZhTw] = "從螢幕吸色",
			[LanguageCode.En] = "Pick Color",
			[LanguageCode.Ja] = "画面から吸色"
		};
		dictionary["ColorPickerSwatches"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "预设经典配色卡 (Quick Swatches - 滚轮滚动查看全部):",
			[LanguageCode.ZhTw] = "預設經典配色卡 (Quick Swatches - 滾輪滾動查看全部):",
			[LanguageCode.En] = "Preset Color Swatches (Scroll to browse):",
			[LanguageCode.Ja] = "プリセットカラーパレット (スクロールで全表示):"
		};
		dictionary["ColorPickerApply"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "应用色彩",
			[LanguageCode.ZhTw] = "套用色彩",
			[LanguageCode.En] = "Apply Color",
			[LanguageCode.Ja] = "色を適用"
		};
		dictionary["InputDialogTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置方案 - StarPie",
			[LanguageCode.ZhTw] = "配置方案 - StarPie",
			[LanguageCode.En] = "Profile - StarPie",
			[LanguageCode.Ja] = "プロファイル - StarPie"
		};
		dictionary["InputDialogEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "名称不能为空，请输入有效的配置名称。",
			[LanguageCode.ZhTw] = "名稱不能為空，請輸入有效的配置名稱。",
			[LanguageCode.En] = "Name cannot be empty. Please enter a valid profile name.",
			[LanguageCode.Ja] = "名前を入力してください。"
		};
		dictionary["Notice"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示",
			[LanguageCode.ZhTw] = "提示",
			[LanguageCode.En] = "Notice",
			[LanguageCode.Ja] = "お知らせ"
		};
		dictionary["Error"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "错误",
			[LanguageCode.ZhTw] = "錯誤",
			[LanguageCode.En] = "Error",
			[LanguageCode.Ja] = "エラー"
		};
		dictionary["AppName"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie",
			[LanguageCode.ZhTw] = "StarPie",
			[LanguageCode.En] = "StarPie",
			[LanguageCode.Ja] = "StarPie"
		};
		dictionary["AppSubtitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "现代鼠标轮盘笔势系统",
			[LanguageCode.ZhTw] = "現代滑鼠輪盤手勢系統",
			[LanguageCode.En] = "Modern Mouse Radial Gestures",
			[LanguageCode.Ja] = "次世代マウスラジアルジェスチャー"
		};
		dictionary["WindowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie 设置控制台 (Preferences)",
			[LanguageCode.ZhTw] = "StarPie 設定控制台 (Preferences)",
			[LanguageCode.En] = "StarPie Preferences Console",
			[LanguageCode.Ja] = "StarPie 環境設定コンソール"
		};
		dictionary["TabTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfaf 触发与场景",
			[LanguageCode.ZhTw] = "\ud83c\udfaf 觸發與場景",
			[LanguageCode.En] = "\ud83c\udfaf Trigger & Scenes",
			[LanguageCode.Ja] = "\ud83c\udfaf トリガーとシーン"
		};
		dictionary["TabAppearance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 外观与形态",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 外觀與形態",
			[LanguageCode.En] = "\ud83c\udfa8 Appearance & Shapes",
			[LanguageCode.Ja] = "\ud83c\udfa8 外観と形状"
		};
		dictionary["TabGestures"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 手势与动作",
			[LanguageCode.ZhTw] = "⚡ 手勢與動作",
			[LanguageCode.En] = "⚡ Gestures & Actions",
			[LanguageCode.Ja] = "⚡ ジェスチャーと動作"
		};
		dictionary["TabAdvanced"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 高级与系统",
			[LanguageCode.ZhTw] = "⚙\ufe0f 進階與系統",
			[LanguageCode.En] = "⚙\ufe0f Advanced & System",
			[LanguageCode.Ja] = "⚙\ufe0f 高度な設定とシステム"
		};
		dictionary["TabAbout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udccb 关于与更新",
			[LanguageCode.ZhTw] = "\ud83d\udccb 關於與更新",
			[LanguageCode.En] = "\ud83d\udccb About & Updates",
			[LanguageCode.Ja] = "\ud83d\udccb 情報と更新"
		};
		dictionary["SidebarCollapse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "折叠侧边栏",
			[LanguageCode.ZhTw] = "摺疊側邊欄",
			[LanguageCode.En] = "Collapse sidebar",
			[LanguageCode.Ja] = "サイドバーを折りたたむ"
		};
		dictionary["SidebarExpand"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "展开侧边栏",
			[LanguageCode.ZhTw] = "展開側邊欄",
			[LanguageCode.En] = "Expand sidebar",
			[LanguageCode.Ja] = "サイドバーを展開"
		};
		dictionary["BottomStatusNote"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "注: 所有修改均在内存中即时生效，点击【保存更改】持久化保存至硬盘。",
			[LanguageCode.ZhTw] = "註: 所有修改均在記憶體中即時生效，點擊【儲存變更】持久化儲存至硬碟。",
			[LanguageCode.En] = "Note: All changes take effect in memory immediately. Click [Save Changes] to persist to disk.",
			[LanguageCode.Ja] = "注: 変更はメモリ上で即座に有効になります。[変更を保存] で設定ファイルに永続化されます。"
		};
		dictionary["BtnSave"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "保存更改",
			[LanguageCode.ZhTw] = "儲存變更",
			[LanguageCode.En] = "Save Changes",
			[LanguageCode.Ja] = "変更を保存"
		};
		dictionary["BtnClose"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关闭并隐藏",
			[LanguageCode.ZhTw] = "關閉並隱藏",
			[LanguageCode.En] = "Close & Hide",
			[LanguageCode.Ja] = "閉じて隠す"
		};
		dictionary["TriggerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "触发与场景隔离设置",
			[LanguageCode.ZhTw] = "觸發與場景隔離設定",
			[LanguageCode.En] = "Trigger & Scene Isolation",
			[LanguageCode.Ja] = "トリガーとシーンの分離設定"
		};
		dictionary["TriggerSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在此配置全局鼠标手势的触发灵敏度、全屏游戏自动拦截与排除程序黑名单。",
			[LanguageCode.ZhTw] = "在此配置全域滑鼠手勢的觸發靈敏度、全螢幕遊戲自動攔截與排除程式黑名單。",
			[LanguageCode.En] = "Configure mouse gesture sensitivity, full-screen gaming bypass, and exclusion blacklist.",
			[LanguageCode.Ja] = "マウスジェスチャーの感度、フルスクリーンゲームでの自動回避、除外プロセスを設定します。"
		};
		dictionary["TriggerRecorderTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘唤醒触发按键 & 组合键录制",
			[LanguageCode.ZhTw] = "輪盤喚醒觸發按鍵 & 組合鍵錄製",
			[LanguageCode.En] = "Radial Menu Trigger & Combo Key Recorder",
			[LanguageCode.Ja] = "ラジアルメニュー起動トリガー＆コンボキー録画"
		};
		dictionary["TriggerRecorderDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "支持鼠标所有按键（右键/中键/侧键1/侧键2/左键）与键盘单键（如 CapsLock、波浪键~、空格、字母键、F区键等）及组合键一键物理录制绑定。绑定鼠标左键后长按可唤醒轮盘，单机左键保持系统正常点击。未触发手势的轻点将自动放行原生按键点击。建议避免将常用功能按键绑定为触发按键。",
			[LanguageCode.ZhTw] = "支持滑鼠所有按鍵（右鍵/中鍵/側鍵1/側鍵2/左鍵）與鍵盤單鍵（如 CapsLock、波浪鍵~、空格、字母鍵、F區鍵等）及組合鍵一鍵物理錄製綁定。綁定滑鼠左鍵後長按可喚醒輪盤，單擊左鍵保持系統正常點擊。未觸發手勢的輕點將自動放行原生按鍵點擊。建議避免將常用功能按鍵綁定為觸發按鍵。",
			[LanguageCode.En] = "Supports one-click physical recording for all mouse buttons (Right, Middle, Side 1/2, Left) and keyboard keys as well as combos. When Left Button is bound, long-press opens radial menu, while quick click maintains normal click function. It is recommended to avoid binding frequently used functional keys as trigger keys.",
			[LanguageCode.Ja] = "すべてのマウスボタン（右、中央、サイド1/2、左）およびキーボード単キーやコンボの物理録画に対応。左ボタン設定時は長押しでホイール起動、短押しクリックは通常の操作を維持します。常用する機能キーを起動トリガーに割り当てることは避けることを推奨します。"
		};
		dictionary["TriggerLeftButtonRecordedTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🟢 已成功绑定【鼠标左键】：长按左键唤醒轮盘，单机左键保持系统正常点击！",
			[LanguageCode.ZhTw] = "🟢 已成功綁定【滑鼠左鍵】：長按左鍵喚醒輪盤，單擊左鍵保持系統正常點擊！",
			[LanguageCode.En] = "🟢 [Left Mouse Button] bound: Long-press to open radial menu, quick click maintains normal click function!",
			[LanguageCode.Ja] = "🟢 【マウス左ボタン】設定完了：長押しでホイール起動、短押しクリックは通常の操作を維持します！"
		};
		dictionary["ProfileTriggerEnable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 为此软件单独设置唤醒键",
			[LanguageCode.ZhTw] = "⚡ 為此軟體單獨設定喚醒鍵",
			[LanguageCode.En] = "⚡ Set a dedicated wake-up key for this app",
			[LanguageCode.Ja] = "⚡ このアプリ専用の起動キーを設定"
		};
		dictionary["ProfileTriggerFollowGlobal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随全局",
			[LanguageCode.ZhTw] = "跟隨全域",
			[LanguageCode.En] = "Follow global",
			[LanguageCode.Ja] = "グローバルに追従"
		};
		dictionary["ProfileTriggerRecord"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔴 录制",
			[LanguageCode.ZhTw] = "🔴 錄製",
			[LanguageCode.En] = "🔴 Record",
			[LanguageCode.Ja] = "🔴 記録"
		};
		dictionary["ProfileTriggerClear"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✖ 清除",
			[LanguageCode.ZhTw] = "✖ 清除",
			[LanguageCode.En] = "✖ Clear",
			[LanguageCode.Ja] = "✖ クリア"
		};
		dictionary["ProfileTriggerTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未单独设置时，此软件唤醒轮盘使用全局「触发与场景」的快捷按键。",
			[LanguageCode.ZhTw] = "未單獨設定時，此軟體喚醒輪盤使用全域「觸發與場景」的快捷按鍵。",
			[LanguageCode.En] = "When not set separately, this app uses the global \"Trigger & Scene\" shortcut key to open the radial menu.",
			[LanguageCode.Ja] = "個別設定しない場合、このアプリはグローバルの「トリガーとシーン」ショートカットキーでホイールを起動します。"
		};
		dictionary["LongPressTriggerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "长按触发按键呼出面板 (可选)",
			[LanguageCode.ZhTw] = "長按觸發按鍵呼叫面板 (可選)",
			[LanguageCode.En] = "Long-press trigger to open the menu (optional)",
			[LanguageCode.Ja] = "長押しでメニューを開く (任意)"
		};
		dictionary["LongPressTriggerDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住触发键不动超过设定时长即呼出轮盘；未达时长松手仍为普通按键。与按住拖动呼出共存。",
			[LanguageCode.ZhTw] = "按住觸發鍵不動超過設定時長即呼叫輪盤；未達時長鬆手仍為普通按鍵。與按住拖動呼叫共存。",
			[LanguageCode.En] = "Hold the trigger key still for the configured duration to open the wheel. Releasing early still behaves as a normal key press. Coexists with the drag-to-open gesture.",
			[LanguageCode.Ja] = "トリガーキーを一定時間押し続けるとホイールが開きます。時間前に離すと通常のキー操作になります。ドラッグで開く動作と共存できます。"
		};
		dictionary["GestureTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "鼠标手势 (Mouse Gestures)",
			[LanguageCode.ZhTw] = "滑鼠手勢 (Mouse Gestures)",
			[LanguageCode.En] = "Mouse Gestures",
			[LanguageCode.Ja] = "マウスジェスチャー"
		};
		dictionary["GestureDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用后，按住手势触发键画出轨迹（如 ↓、→、L 形），释放即执行对应动作；支持单段、双段与三段图样。轻点仍透传原生点击。",
			[LanguageCode.ZhTw] = "啟用後，按住手勢觸發鍵畫出軌跡（如 ↓、→、L 形），釋放即執行對應動作；支援單段、雙段與三段圖樣。輕點仍透傳原生點擊。",
			[LanguageCode.En] = "Hold the gesture trigger button and draw a trail (e.g. ↓, →, L-shape); releasing executes the mapped action. Supports single, double and triple-segment patterns. Quick clicks still pass through.",
			[LanguageCode.Ja] = "トリガーキーを押しながら軌跡を描くと（↓、→、L字など）、離した時点で割り当てたアクションを実行します。1〜3セグメントのジェスチャーに対応。短押しは通常通り通過します。"
		};
		dictionary["GestureEnableText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用鼠标手势模式",
			[LanguageCode.ZhTw] = "啟用滑鼠手勢模式",
			[LanguageCode.En] = "Enable mouse gesture mode",
			[LanguageCode.Ja] = "マウスジェスチャーモードを有効化"
		};
		dictionary["GestureEnableDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发键将不再弹出轮盘，仅用于绘制手势；其它触发键照常弹轮盘。",
			[LanguageCode.ZhTw] = "手勢觸發鍵將不再彈出輪盤，僅用於繪製手勢；其它觸發鍵照常彈輪盤。",
			[LanguageCode.En] = "The gesture trigger button no longer opens the wheel; it is used for drawing gestures only. Other triggers keep opening the wheel.",
			[LanguageCode.Ja] = "ジェスチャートリガーキーはホイールを開かず、ジェスチャー描画専用になります。他のトリガーは従来通りホイールを開きます。"
		};
		dictionary["GestureTriggerLabelText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发键：",
			[LanguageCode.ZhTw] = "手勢觸發鍵：",
			[LanguageCode.En] = "Gesture trigger button:",
			[LanguageCode.Ja] = "ジェスチャートリガーキー："
		};
		dictionary["GestureHintPlaceText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "松手提示位置：",
			[LanguageCode.ZhTw] = "鬆手提示位置：",
			[LanguageCode.En] = "Release-hint position:",
			[LanguageCode.Ja] = "離した時のヒント位置："
		};
		dictionary["GestureSensitivityTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势段灵敏度：",
			[LanguageCode.ZhTw] = "手勢段靈敏度：",
			[LanguageCode.En] = "Gesture segment sensitivity:",
			[LanguageCode.Ja] = "ジェスチャー感度："
		};
		dictionary["CancelActionTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "外甩取消时执行的动作",
			[LanguageCode.ZhTw] = "外甩取消時執行的動作",
			[LanguageCode.En] = "Action on Outer-Escape Cancel",
			[LanguageCode.Ja] = "外側スワイプキャンセル時の動作"
		};
		dictionary["CancelActionDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅当通过「外甩取消」（向外甩出且未选中任何动作）时执行这个自定义动作；回到中心取消按钮松手仍为默认静默关闭。",
			[LanguageCode.ZhTw] = "僅當透過「外甩取消」（向外甩出且未選中任何動作）時執行這個自訂動作；回到中心取消按鈕鬆手仍為預設靜默關閉。",
			[LanguageCode.En] = "Only when you cancel by flinging outward (nothing selected) does this custom action run; releasing over the center cancel button still just closes the menu silently.",
			[LanguageCode.Ja] = "外側へフリックしてキャンセルした場合（何も選択していない）のみカスタムアクションを実行します。中央のキャンセルで離すと従来通りサイレントクローズします。"
		};
		dictionary["CancelActionEnableText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用自定义外甩取消动作",
			[LanguageCode.ZhTw] = "啟用自訂外甩取消動作",
			[LanguageCode.En] = "Enable custom outer-escape cancel action",
			[LanguageCode.Ja] = "外側スワイプキャンセル時のカスタム動作を有効化"
		};
		dictionary["ActionTypeTileShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺窗口",
			[LanguageCode.ZhTw] = "平鋪視窗",
			[LanguageCode.En] = "Tile Windows",
			[LanguageCode.Ja] = "ウィンドウを並べる"
		};
		dictionary["TileLayout2L"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左右对半",
			[LanguageCode.ZhTw] = "左右對半",
			[LanguageCode.En] = "Left-Right",
			[LanguageCode.Ja] = "左右分割"
		};
		dictionary["TileLayout2T"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上下对半",
			[LanguageCode.ZhTw] = "上下對半",
			[LanguageCode.En] = "Top-Bottom",
			[LanguageCode.Ja] = "上下分割"
		};
		dictionary["TileLayout3L12"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左大列 + 右上/右下",
			[LanguageCode.ZhTw] = "左大列 + 右上/右下",
			[LanguageCode.En] = "Big Left + Right Two",
			[LanguageCode.Ja] = "左大＋右二"
		};
		dictionary["TileLayout3R21"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "右大列 + 左上/左下",
			[LanguageCode.ZhTw] = "右大列 + 左上/左下",
			[LanguageCode.En] = "Big Right + Left Two",
			[LanguageCode.Ja] = "右大＋左二"
		};
		dictionary["TileLayout3R"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "三等分竖列",
			[LanguageCode.ZhTw] = "三等分豎列",
			[LanguageCode.En] = "Three Columns",
			[LanguageCode.Ja] = "3等分列"
		};
		dictionary["TileLayout4G"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "四宫格 2×2",
			[LanguageCode.ZhTw] = "四宮格 2×2",
			[LanguageCode.En] = "2×2 Grid",
			[LanguageCode.Ja] = "2×2 グリッド"
		};
		dictionary["TileLayout6G"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "六宫格 3×2",
			[LanguageCode.ZhTw] = "六宮格 3×2",
			[LanguageCode.En] = "3×2 Grid",
			[LanguageCode.Ja] = "3×2 グリッド"
		};
		dictionary["ActionTypeWindowManagerShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口管理",
			[LanguageCode.ZhTw] = "視窗管理",
			[LanguageCode.En] = "Window Management",
			[LanguageCode.Ja] = "ウィンドウ管理"
		};
		dictionary["ActionTypeTileRestoreShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "恢复上次平铺",
			[LanguageCode.ZhTw] = "還原上次平鋪",
			[LanguageCode.En] = "Restore Tiles",
			[LanguageCode.Ja] = "前の配置に戻す"
		};
		dictionary["ActionTypeMoveMonitorShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口移到下一屏",
			[LanguageCode.ZhTw] = "視窗移到下一螢幕",
			[LanguageCode.En] = "Move to Next Monitor",
			[LanguageCode.Ja] = "次のモニターへ移動"
		};
		dictionary["ActionTypeTopmostShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口置顶/取消置顶",
			[LanguageCode.ZhTw] = "視窗置頂/取消置頂",
			[LanguageCode.En] = "Toggle Always-on-Top",
			[LanguageCode.Ja] = "最前面表示の切替"
		};
		dictionary["ActionTypeOpacityShort"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口透明度",
			[LanguageCode.ZhTw] = "視窗透明度",
			[LanguageCode.En] = "Window Opacity",
			[LanguageCode.Ja] = "ウィンドウの透明度"
		};
		dictionary["TileCycleLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环切换布局",
			[LanguageCode.ZhTw] = "循環切換佈局",
			[LanguageCode.En] = "Cycle layouts",
			[LanguageCode.Ja] = "レイアウトを順番に切替"
		};
		dictionary["TileGlobalTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺窗口设置",
			[LanguageCode.ZhTw] = "平鋪視窗設定",
			[LanguageCode.En] = "Tiling Settings",
			[LanguageCode.Ja] = "タイリング設定"
		};
		dictionary["TileGlobalDescText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "「平铺窗口」动作的全局行为（屏幕边距、窗口间距、包含最小化窗口、排除名单与多布局循环切换）。",
			[LanguageCode.ZhTw] = "「平鋪視窗」動作的全域行為（螢幕邊距、視窗間距、包含最小化視窗、排除名單與多佈局循環切換）。",
			[LanguageCode.En] = "Global behaviour of the Tile Windows action (screen margins, window gaps, minimized windows, exclusion list and layout cycling).",
			[LanguageCode.Ja] = "「ウィンドウを並べる」アクションの全体挙動（画面マージン、ウィンドウ間隔、最小化ウィンドウの包含、除外リスト、レイアウト切替）。"
		};
		dictionary["TileMarginText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "屏幕边距（上 / 下 / 左 / 右，物理像素，0 = 贴边）：",
			[LanguageCode.ZhTw] = "螢幕邊距（上 / 下 / 左 / 右，物理像素，0 = 貼邊）：",
			[LanguageCode.En] = "Screen margins (Top / Bottom / Left / Right, physical pixels, 0 = flush):",
			[LanguageCode.Ja] = "画面マージン（上 / 下 / 左 / 右、物理ピクセル、0 = 端に寄せる）："
		};
		dictionary["TileGapText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "窗口间距（物理像素，0 = 紧贴）：",
			[LanguageCode.ZhTw] = "視窗間距（物理像素，0 = 緊貼）：",
			[LanguageCode.En] = "Gap between windows (physical pixels, 0 = flush):",
			[LanguageCode.Ja] = "ウィンドウ間隔（物理ピクセル、0 = 隙間なし）："
		};
		dictionary["TileMinimizeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "包含最小化窗口（还原后参与平铺）",
			[LanguageCode.ZhTw] = "包含最小化視窗（還原後參與平鋪）",
			[LanguageCode.En] = "Include minimized windows (restore into the layout)",
			[LanguageCode.Ja] = "最小化ウィンドウも含める（復元して配置）"
		};
		dictionary["TileExcludeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平铺排除名单（进程 exe 名，逗号分隔，如 notepad,spotify）：",
			[LanguageCode.ZhTw] = "平鋪排除名單（程序 exe 名，逗號分隔，如 notepad,spotify）：",
			[LanguageCode.En] = "Tiling exclusion list (process exe names, comma-separated, e.g. notepad,spotify):",
			[LanguageCode.Ja] = "並べる対象外のプロセス（exe名、カンマ区切り例 notepad,spotify）："
		};
		dictionary["TileRestoreAllLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "还原所有窗口（回到平铺前）",
			[LanguageCode.ZhTw] = "還原所有視窗（回到平鋪前）",
			[LanguageCode.En] = "Restore all windows (pre-tile state)",
			[LanguageCode.Ja] = "すべてのウィンドウを元に戻す（並べる前の状態）"
		};
		dictionary["TileCycleBackLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环返回",
			[LanguageCode.ZhTw] = "循環返回",
			[LanguageCode.En] = "Cycle previous",
			[LanguageCode.Ja] = "前のレイアウトへ"
		};
		dictionary["TileLayoutML"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居左 + 右栈",
			[LanguageCode.ZhTw] = "主窗居左 + 右棧",
			[LanguageCode.En] = "Master Left + Stack",
			[LanguageCode.Ja] = "マスター左＋スタック"
		};
		dictionary["TileLayoutMR"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居右 + 左栈",
			[LanguageCode.ZhTw] = "主窗居右 + 左棧",
			[LanguageCode.En] = "Master Right + Stack",
			[LanguageCode.Ja] = "マスター右＋スタック"
		};
		dictionary["TileLayoutMT"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居上 + 下栈",
			[LanguageCode.ZhTw] = "主窗居上 + 下棧",
			[LanguageCode.En] = "Master Top + Stack",
			[LanguageCode.Ja] = "マスター上＋スタック"
		};
		dictionary["TileLayoutMB"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主窗居下 + 上栈",
			[LanguageCode.ZhTw] = "主窗居下 + 上棧",
			[LanguageCode.En] = "Master Bottom + Stack",
			[LanguageCode.Ja] = "マスター下＋スタック"
		};
		dictionary["TileLayoutMO"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "单窗全屏 (Monocle)",
			[LanguageCode.ZhTw] = "單窗全螢幕 (Monocle)",
			[LanguageCode.En] = "Monocle",
			[LanguageCode.Ja] = "モノクル（全画面）"
		};
		dictionary["TileLayoutHS"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "上主 50% + 下栈 (H-Stack)",
			[LanguageCode.ZhTw] = "上主 50% + 下棧 (H-Stack)",
			[LanguageCode.En] = "Master Top + Stack (H-Stack)",
			[LanguageCode.Ja] = "上マスター＋下スタック"
		};
		dictionary["TileLayoutVS"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "左主 50% + 右栈 (V-Stack)",
			[LanguageCode.ZhTw] = "左主 50% + 右棧 (V-Stack)",
			[LanguageCode.En] = "Master Left + Stack (V-Stack)",
			[LanguageCode.Ja] = "左マスター＋右スタック"
		};
		dictionary["TileLayoutCOL"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "等宽竖列 (Columns)",
			[LanguageCode.ZhTw] = "等寬豎列 (Columns)",
			[LanguageCode.En] = "Columns",
			[LanguageCode.Ja] = "等幅カラム"
		};
		dictionary["TileLayoutBSP"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二叉分割 (BSP)",
			[LanguageCode.ZhTw] = "二叉分割 (BSP)",
			[LanguageCode.En] = "Binary Split (BSP)",
			[LanguageCode.Ja] = "二分木分割 (BSP)"
		};
		dictionary["TileLayoutAG"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自适应网格 (A-Grid)",
			[LanguageCode.ZhTw] = "自適應網格 (A-Grid)",
			[LanguageCode.En] = "Auto Grid",
			[LanguageCode.Ja] = "自動グリッド"
		};
		dictionary["TileCycleRangeText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "循环切换范围（布局 key 逗号分隔，空=全部，如 2L,2T,4G,ML）：",
			[LanguageCode.ZhTw] = "循環切換範圍（佈局 key 逗號分隔，空=全部，如 2L,2T,4G,ML）：",
			[LanguageCode.En] = "Cycle range (layout keys, comma-separated; empty = all, e.g. 2L,2T,4G,ML):",
			[LanguageCode.Ja] = "循環切替範囲（レイアウトキーをカンマ区切り。空=全て。例 2L,2T,4G,ML）："
		};
		dictionary["GestureMappingTitleText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势图样映射",
			[LanguageCode.ZhTw] = "手勢圖樣映射",
			[LanguageCode.En] = "Gesture Pattern Mappings",
			[LanguageCode.Ja] = "ジェスチャーパターン割り当て"
		};
		dictionary["BtnRecordTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd34 点击录制触发键 / 组合键",
			[LanguageCode.ZhTw] = "\ud83d\udd34 點擊錄製觸發鍵 / 組合鍵",
			[LanguageCode.En] = "\ud83d\udd34 Record Trigger / Combo Key",
			[LanguageCode.Ja] = "\ud83d\udd34 トリガーキー/コンボを録画"
		};
		dictionary["BtnResetDefaultTrigger"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd04 恢复默认 (鼠标右键)",
			[LanguageCode.ZhTw] = "\ud83d\udd04 恢復默認 (滑鼠右鍵)",
			[LanguageCode.En] = "\ud83d\udd04 Reset Default (Right Mouse)",
			[LanguageCode.Ja] = "\ud83d\udd04 デフォルトに戻す (マウス右ボタン)"
		};
		dictionary["CurrentBindingLabel"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当前生效触发键：",
			[LanguageCode.ZhTw] = "當前生效觸發鍵：",
			[LanguageCode.En] = "Active Trigger Binding:",
			[LanguageCode.Ja] = "現在の有効トリガー："
		};
		dictionary["TriggerButtonTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘唤醒触发按键",
			[LanguageCode.ZhTw] = "輪盤喚醒觸發按鍵",
			[LanguageCode.En] = "Radial Menu Trigger Button",
			[LanguageCode.Ja] = "ラジアルメニュー起動トリガーボタン"
		};
		dictionary["TriggerButtonDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择按住并拖动唤醒轮盘手势的鼠标按键。未触发手势的轻点将自动放行原生按键点击。",
			[LanguageCode.ZhTw] = "選擇按住並拖動喚醒輪盤手勢的滑鼠按鍵。未觸發手勢的輕點將自動放行原生按鍵點擊。",
			[LanguageCode.En] = "Select which mouse button to hold and drag to summon the radial menu. Quick clicks without dragging will naturally pass through native click events.",
			[LanguageCode.Ja] = "長押しドラッグでラジアルメニューを起動するマウスボタンを選択します。短押しクリックは通常のクリックとして処理されます。"
		};
		dictionary["TriggerBtnRight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddb1\ufe0f 鼠标右键 (Right Button) [推荐 / 默认]",
			[LanguageCode.ZhTw] = "\ud83d\uddb1\ufe0f 滑鼠右鍵 (Right Button) [推薦 / 默認]",
			[LanguageCode.En] = "\ud83d\uddb1\ufe0f Right Mouse Button [Default / Recommended]",
			[LanguageCode.Ja] = "\ud83d\uddb1\ufe0f マウス右ボタン [推奨 / デフォルト]"
		};
		dictionary["TriggerBtnMiddle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddb1\ufe0f 鼠标中键 / 滚轮按压 (Middle Button)",
			[LanguageCode.ZhTw] = "\ud83d\uddb1\ufe0f 滑鼠中鍵 / 滾輪按壓 (Middle Button)",
			[LanguageCode.En] = "\ud83d\uddb1\ufe0f Middle Mouse Button / Wheel Click",
			[LanguageCode.Ja] = "\ud83d\uddb1\ufe0f マウス中央ボタン / ホイールクリック"
		};
		dictionary["TriggerBtnX1"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddb1\ufe0f 鼠标侧键 1 / 后退键 (XButton 1 / Back)",
			[LanguageCode.ZhTw] = "\ud83d\uddb1\ufe0f 滑鼠側鍵 1 / 後退鍵 (XButton 1 / Back)",
			[LanguageCode.En] = "\ud83d\uddb1\ufe0f Mouse Side Button 1 / Back (XButton 1)",
			[LanguageCode.Ja] = "\ud83d\uddb1\ufe0f マウスサイドボタン 1 / 戻る (XButton 1)"
		};
		dictionary["TriggerBtnX2"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddb1\ufe0f 鼠标侧键 2 / 前进键 (XButton 2 / Forward)",
			[LanguageCode.ZhTw] = "\ud83d\uddb1\ufe0f 滑鼠側鍵 2 / 前進鍵 (XButton 2 / Forward)",
			[LanguageCode.En] = "\ud83d\uddb1\ufe0f Mouse Side Button 2 / Forward (XButton 2)",
			[LanguageCode.Ja] = "\ud83d\uddb1\ufe0f マウスサイドボタン 2 / 進む (XButton 2)"
		};
		dictionary["SensitivityTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势触发灵敏度",
			[LanguageCode.ZhTw] = "手勢觸發靈敏度",
			[LanguageCode.En] = "Trigger Sensitivity",
			[LanguageCode.Ja] = "ジェスチャー起動感度"
		};
		dictionary["SensitivityDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住鼠标右键移动超过指定像素距离后呼出手势轮盘。距离越小越灵敏，过小可能造成右键微抖动误触。",
			[LanguageCode.ZhTw] = "按住滑鼠右鍵移動超過指定像素距離後呼出手勢輪盤。距離越小越靈敏，過小可能造成右鍵微抖動誤觸。",
			[LanguageCode.En] = "Hold right-click and move beyond this pixel distance to trigger radial menu. Lower values are more sensitive.",
			[LanguageCode.Ja] = "右クリックを押しながら指定ピクセル以上移動するとホイールを呼び出します。値が小さいほど高感度です。"
		};
		dictionary["SceneIsolationTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "场景隔离与防误触",
			[LanguageCode.ZhTw] = "場景隔離與防誤觸",
			[LanguageCode.En] = "Scene Isolation & Guard",
			[LanguageCode.Ja] = "シーン分離と誤操作防止"
		};
		dictionary["SceneIsolationDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "当处于特定场景或配合修饰键操作时，自动绕过轮盘拦截，放行原生右键事件。",
			[LanguageCode.ZhTw] = "當處於特定場景或配合修飾鍵操作時，自動繞過輪盤攔截，放行原生右鍵事件。",
			[LanguageCode.En] = "Automatically bypass radial menu and pass-through native right-click in specific scenarios.",
			[LanguageCode.Ja] = "特定の環境や修飾キー操作時にホイールを無効化し、通常の右クリックを通過させます。"
		};
		dictionary["FullScreenOption"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全屏游戏/独占应用自动禁用手势",
			[LanguageCode.ZhTw] = "全螢幕遊戲/獨佔應用自動禁用手勢",
			[LanguageCode.En] = "Auto-disable in Full-screen games / Exclusive apps",
			[LanguageCode.Ja] = "全画面ゲーム/専用アプリでジェスチャーを自動無効化"
		};
		dictionary["FullScreenOptionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动检测当前前台窗口是否处于全屏独占状态，避免游戏瞄准等右键操作被拦截。",
			[LanguageCode.ZhTw] = "自動檢測當前前台視窗是否處於全螢幕獨佔狀態，避免遊戲瞄準等右鍵操作被攔截。",
			[LanguageCode.En] = "Detects whether active window is running in full-screen to avoid intercepting gaming right-clicks.",
			[LanguageCode.Ja] = "アクティブなウィンドウが全画面かどうかを検知し、ゲームの照準等の右クリック操作を邪魔しません。"
		};
		dictionary["ModifierPassTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "快捷键旁路穿透 (按住以下修饰键拖拽时不触发手势):",
			[LanguageCode.ZhTw] = "快速鍵旁路穿透 (按住以下修飾鍵拖曳時不觸發手勢):",
			[LanguageCode.En] = "Modifier Pass-Through (hold to bypass gestures):",
			[LanguageCode.Ja] = "修飾キーバイパス (押下中はジェスチャーを無効化):"
		};
		dictionary["ModifierCtrl"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Ctrl 键时旁路",
			[LanguageCode.ZhTw] = "按住 Ctrl 鍵時旁路",
			[LanguageCode.En] = "Bypass on Ctrl",
			[LanguageCode.Ja] = "Ctrl 押下時にバイパス"
		};
		dictionary["ModifierShift"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Shift 键时旁路",
			[LanguageCode.ZhTw] = "按住 Shift 鍵時旁路",
			[LanguageCode.En] = "Bypass on Shift",
			[LanguageCode.Ja] = "Shift 押下時にバイパス"
		};
		dictionary["ModifierAlt"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Alt 键时旁路",
			[LanguageCode.ZhTw] = "按住 Alt 鍵時旁路",
			[LanguageCode.En] = "Bypass on Alt",
			[LanguageCode.Ja] = "Alt 押下時にバイパス"
		};
		dictionary["BlacklistTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "进程排除黑名单",
			[LanguageCode.ZhTw] = "行程排除黑名單",
			[LanguageCode.En] = "Process Exclusion Blacklist",
			[LanguageCode.Ja] = "除外プロセスブラックリスト"
		};
		dictionary["BlacklistDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在排除名单中的应用程序（如远程桌面、画图、3D建模软件）中，完全放行鼠标右键。",
			[LanguageCode.ZhTw] = "在排除名單中的應用程式（如遠端桌面、小畫家、3D建模軟體）中，完全放行滑鼠右鍵。",
			[LanguageCode.En] = "Native right-click is fully allowed within blacklisted applications (e.g. Remote Desktop, Paint, CAD).",
			[LanguageCode.Ja] = "登録されたアプリ（リモートデスクトップ、ペイント、3Dモデリング等）では右クリックを直接通します。"
		};
		dictionary["BtnAddProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "➕ 添加进程",
			[LanguageCode.ZhTw] = "➕ 新增處理程序",
			[LanguageCode.En] = "➕ Add Process",
			[LanguageCode.Ja] = "➕ プロセス追加"
		};
		dictionary["BtnPickProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 选择应用...",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 選擇應用程式...",
			[LanguageCode.En] = "\ud83d\udd0d Select App...",
			[LanguageCode.Ja] = "\ud83d\udd0d アプリを選択..."
		};
		dictionary["BtnDeleteProcess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 移除选中",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 移除選取",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Remove Selected",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f 選択項目を削除"
		};
		dictionary["BlacklistPlaceholder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "输入进程名称 (如 solidworks.exe) 或点击右侧选择应用...",
			[LanguageCode.ZhTw] = "輸入處理程序名稱 (如 solidworks.exe) 或點擊右側選擇應用程式...",
			[LanguageCode.En] = "Enter process name (e.g. solidworks.exe) or browse...",
			[LanguageCode.Ja] = "プロセス名を入力 (例: solidworks.exe) またはアプリを選択..."
		};
		dictionary["AppearanceHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘外观与形态定制",
			[LanguageCode.ZhTw] = "輪盤外觀與形態自訂",
			[LanguageCode.En] = "Appearance & Shapes Customization",
			[LanguageCode.Ja] = "外観と形状のカスタマイズ"
		};
		dictionary["AppearanceSubheader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自由配置轮盘视觉风格、配色方案、高亮边缘光晕、几何切削、图标排版与中心核圆贴图。",
			[LanguageCode.ZhTw] = "自由配置輪盤視覺風格、配色方案、高亮邊緣光暈、幾何切削、圖示排版與中心核圓貼圖。",
			[LanguageCode.En] = "Customize visual styles, color palettes, highlight glow, geometry shapes, typography, and core image.",
			[LanguageCode.Ja] = "ビジュアルスタイル、配色テーマ、グロー発光、幾何学形状、アイコン配置、コアバッジをカスタマイズします。"
		};
		dictionary["StyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘渲染风格 (Visual Renderer)",
			[LanguageCode.ZhTw] = "輪盤渲染風格 (Visual Renderer)",
			[LanguageCode.En] = "Visual Renderer Style",
			[LanguageCode.Ja] = "ビジュアルレンダラー"
		};
		dictionary["StyleGlass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "液态毛玻璃 (Glassmorphism)",
			[LanguageCode.ZhTw] = "液態毛玻璃 (Glassmorphism)",
			[LanguageCode.En] = "Liquid Glassmorphism",
			[LanguageCode.Ja] = "リキッドグラスモーフィズム"
		};
		dictionary["StyleClassic"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "经典圆环 (Classic Ring)",
			[LanguageCode.ZhTw] = "經典圓環 (Classic Ring)",
			[LanguageCode.En] = "Classic Ring",
			[LanguageCode.Ja] = "クラシックリング"
		};
		dictionary["StyleClean"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简扇区 (Clean Sectors)",
			[LanguageCode.ZhTw] = "極簡扇區 (Clean Sectors)",
			[LanguageCode.En] = "Clean Sectors",
			[LanguageCode.Ja] = "クリーンセクター"
		};
		dictionary["StyleCatPaw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "萌宠猫爪 (Cute Cat Paw)",
			[LanguageCode.ZhTw] = "萌寵貓爪 (Cute Cat Paw)",
			[LanguageCode.En] = "Cute Cat Paw",
			[LanguageCode.Ja] = "キュートキャットポー (肉球)"
		};
		dictionary["ThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘配色方案 (Color Palette)",
			[LanguageCode.ZhTw] = "輪盤配色方案 (Color Palette)",
			[LanguageCode.En] = "Wheel Color Palette",
			[LanguageCode.Ja] = "ホイール配色パレット"
		};
		dictionary["BtnDeletePreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddd1\ufe0f 删除预设",
			[LanguageCode.ZhTw] = "\ud83d\uddd1\ufe0f 刪除預設",
			[LanguageCode.En] = "\ud83d\uddd1\ufe0f Delete Preset",
			[LanguageCode.Ja] = "\ud83d\uddd1\ufe0f プリセット削除"
		};
		dictionary["GlowTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高亮边缘光晕 (Highlight Edge Glow)",
			[LanguageCode.ZhTw] = "高亮邊緣光暈 (Highlight Edge Glow)",
			[LanguageCode.En] = "Highlight Edge Glow",
			[LanguageCode.Ja] = "ハイライトエッジグロー発光"
		};
		dictionary["GlowFollowTheme"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随主题高亮色 (Auto)",
			[LanguageCode.ZhTw] = "跟隨主題高亮色 (Auto)",
			[LanguageCode.En] = "Follow Theme (Auto)",
			[LanguageCode.Ja] = "テーマ連動 (自動)"
		};
		dictionary["GlowRadius"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕弥散半径 (Glow Radius)",
			[LanguageCode.ZhTw] = "光暈彌散半徑 (Glow Radius)",
			[LanguageCode.En] = "Glow Radius",
			[LanguageCode.Ja] = "グロー拡散半径"
		};
		dictionary["GlowOpacity"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "光晕不透明度 (Glow Opacity)",
			[LanguageCode.ZhTw] = "光暈不透明度 (Glow Opacity)",
			[LanguageCode.En] = "Glow Opacity",
			[LanguageCode.Ja] = "グロー不透明度"
		};
		dictionary["GeometryTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "几何形态与尺寸 (Geometry & Dimensions)",
			[LanguageCode.ZhTw] = "幾何形態與尺寸 (Geometry & Dimensions)",
			[LanguageCode.En] = "Geometry & Dimensions",
			[LanguageCode.Ja] = "幾何学形状とサイズ"
		};
		dictionary["ShapeOriginal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "原生扇区 (Original Sector)",
			[LanguageCode.ZhTw] = "原生扇區 (Original Sector)",
			[LanguageCode.En] = "Original Sector",
			[LanguageCode.Ja] = "オリジナルセクター"
		};
		dictionary["ShapeCircle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简圆形 (Floating Circle)",
			[LanguageCode.ZhTw] = "極簡圓形 (Floating Circle)",
			[LanguageCode.En] = "Floating Circle",
			[LanguageCode.Ja] = "フローティングサークル"
		};
		dictionary["ShapeRounded"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "平滑圆角 (Rounded Fillet)",
			[LanguageCode.ZhTw] = "平滑圓角 (Rounded Fillet)",
			[LanguageCode.En] = "Rounded Fillet",
			[LanguageCode.Ja] = "角丸フィレット"
		};
		dictionary["ShapeCapsule"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "圆润胶囊 (Pill Capsules)",
			[LanguageCode.ZhTw] = "圓潤膠囊 (Pill Capsules)",
			[LanguageCode.En] = "Pill Capsules",
			[LanguageCode.Ja] = "ピルカプセル"
		};
		dictionary["ShapeHexagon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "未来蜂巢 (Hexagon Hive)",
			[LanguageCode.ZhTw] = "未來蜂巢 (Hexagon Hive)",
			[LanguageCode.En] = "Hexagon Hive",
			[LanguageCode.Ja] = "ヘキサゴンハニカム"
		};
		dictionary["RadiusOuter"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘外半径 (Outer Radius)",
			[LanguageCode.ZhTw] = "輪盤外半徑 (Outer Radius)",
			[LanguageCode.En] = "Outer Radius",
			[LanguageCode.Ja] = "外側半径"
		};
		dictionary["RadiusInner"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "内环半径 (Inner Radius)",
			[LanguageCode.ZhTw] = "內環半徑 (Inner Radius)",
			[LanguageCode.En] = "Inner Radius",
			[LanguageCode.Ja] = "内側半径"
		};
		dictionary["RadiusCore"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "核圆半径 (Core Radius)",
			[LanguageCode.ZhTw] = "核圓半徑 (Core Radius)",
			[LanguageCode.En] = "Core Radius",
			[LanguageCode.Ja] = "コア半径"
		};
		dictionary["SectorGap"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区间隙 (Sector Gap)",
			[LanguageCode.ZhTw] = "扇區間隙 (Sector Gap)",
			[LanguageCode.En] = "Sector Gap",
			[LanguageCode.Ja] = "セクター間隔"
		};
		dictionary["SectorCornerRadius"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "扇区倒角 (Corner Radius)",
			[LanguageCode.ZhTw] = "扇區倒角 (Corner Radius)",
			[LanguageCode.En] = "Corner Radius",
			[LanguageCode.Ja] = "角丸半径"
		};
		dictionary["BtnResetGeometry"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "重置形态默认值",
			[LanguageCode.ZhTw] = "重設形態預設值",
			[LanguageCode.En] = "Reset Geometry Defaults",
			[LanguageCode.Ja] = "形状初期値に戻す"
		};
		dictionary["IconLayoutTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标与文字排版 (Layout & Typography)",
			[LanguageCode.ZhTw] = "圖示與文字排版 (Layout & Typography)",
			[LanguageCode.En] = "Layout & Typography",
			[LanguageCode.Ja] = "レイアウトと文字"
		};
		dictionary["LayoutIconText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图文并茂 (Icon + Text)",
			[LanguageCode.ZhTw] = "圖文並茂 (Icon + Text)",
			[LanguageCode.En] = "Icon & Text",
			[LanguageCode.Ja] = "アイコン＋文字"
		};
		dictionary["LayoutIconOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示图标 (Icon Only)",
			[LanguageCode.ZhTw] = "僅顯示圖示 (Icon Only)",
			[LanguageCode.En] = "Icon Only",
			[LanguageCode.Ja] = "アイコンのみ"
		};
		dictionary["LayoutTextOnly"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "仅显示文字 (Text Only)",
			[LanguageCode.ZhTw] = "僅顯示文字 (Text Only)",
			[LanguageCode.En] = "Text Only",
			[LanguageCode.Ja] = "文字のみ"
		};
		dictionary["ShowSectorActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在轮盘扇区中显示动作名称文字",
			[LanguageCode.ZhTw] = "在輪盤扇區中顯示動作名稱文字",
			[LanguageCode.En] = "Show action names in wheel sectors",
			[LanguageCode.Ja] = "ホイールの扇形にアクション名を表示"
		};
		dictionary["ShowSelectedActionText"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选中扇区时在中心显示动作名称",
			[LanguageCode.ZhTw] = "選取扇區時在中心顯示動作名稱",
			[LanguageCode.En] = "Show selected action name in the center",
			[LanguageCode.Ja] = "選択中のアクション名を中央に表示"
		};
		dictionary["SectorIconSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "图标大小 (Icon Size)",
			[LanguageCode.ZhTw] = "圖示大小 (Icon Size)",
			[LanguageCode.En] = "Icon Size",
			[LanguageCode.Ja] = "アイコンサイズ"
		};
		dictionary["SectorFontSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号 (Font Size)",
			[LanguageCode.ZhTw] = "文字字級 (Font Size)",
			[LanguageCode.En] = "Font Size",
			[LanguageCode.Ja] = "文字サイズ"
		};
		dictionary["LayoutTargetGlobal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 全局统一排版",
			[LanguageCode.ZhTw] = "🌐 全域統一排版",
			[LanguageCode.En] = "Global Layout",
			[LanguageCode.Ja] = "全体一括設定"
		};
		dictionary["LayoutTargetSlot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🎯 扇区独立定制",
			[LanguageCode.ZhTw] = "🎯 扇區獨立自訂",
			[LanguageCode.En] = "Slot Custom",
			[LanguageCode.Ja] = "個別カスタマイズ"
		};
		dictionary["EnableSlotCustomLayout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 启用该扇区独立个性化排版",
			[LanguageCode.ZhTw] = "⚡ 啟用該扇區獨立個性化排版",
			[LanguageCode.En] = "Enable custom styling for this slot",
			[LanguageCode.Ja] = "このセクターの個別スタイルを有効化"
		};
		dictionary["ResetToGlobalLayout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复继承全局默认",
			[LanguageCode.ZhTw] = "🔄 恢復繼承全域預設",
			[LanguageCode.En] = "Reset to Global Default",
			[LanguageCode.Ja] = "グローバル設定に戻す"
		};
		dictionary["SectorTextColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘文字颜色 (Sector Text Color):",
			[LanguageCode.ZhTw] = "輪盤文字顏色 (Sector Text Color):",
			[LanguageCode.En] = "Sector Text Color:",
			[LanguageCode.Ja] = "ホイール文字色:"
		};
		dictionary["CoreTextOptions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字与选中显示选项",
			[LanguageCode.ZhTw] = "中心文字與選取顯示選項",
			[LanguageCode.En] = "Center Text & Selection Options",
			[LanguageCode.Ja] = "中央テキストと選択時表示"
		};
		dictionary["CoreFontFamily"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字字体 (Core Font Family):",
			[LanguageCode.ZhTw] = "中心文字字型 (Core Font Family):",
			[LanguageCode.En] = "Center Font Family:",
			[LanguageCode.Ja] = "中央フォント:"
		};
		dictionary["SettingsUiScale"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔍 界面缩放",
			[LanguageCode.ZhTw] = "🔍 介面縮放",
			[LanguageCode.En] = "🔍 UI Scale",
			[LanguageCode.Ja] = "🔍 表示拡大率"
		};
		dictionary["SettingsUiScaleTip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "调节设置控制台界面整体缩放比例 (80% ~ 200%)，高分屏下可放大文字与控件。窗口尺寸保持不变，内容变大后由页面滚动承接；也可随时使用 Ctrl + / Ctrl - 调节，Ctrl 0 复位。",
			[LanguageCode.ZhTw] = "調整設定控制台介面整體縮放比例 (80% ~ 200%)，高解析度螢幕下可放大文字與控件。視窗尺寸保持不變，內容變大後由頁面捲動承接；亦可隨時使用 Ctrl + / Ctrl - 調整，Ctrl 0 復位。",
			[LanguageCode.En] = "Scale the whole settings console between 80% and 200% for better readability on high-resolution screens. The window size stays untouched - enlarged content simply scrolls. Ctrl + / Ctrl - adjusts it anytime, and Ctrl 0 resets.",
			[LanguageCode.Ja] = "設定画面全体の表示拡大率を 80%〜200% で調整できます。高解像度画面での文字・控件の視認性向上に。ウィンドウサイズは変更されず、拡大した内容はスクロールして表示します。Ctrl + / Ctrl - ですぐに調整、Ctrl 0 でリセット。"
		};
		dictionary["CoreFontSize"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字大小 (Core Font Size):",
			[LanguageCode.ZhTw] = "中心文字大小 (Core Font Size):",
			[LanguageCode.En] = "Center Font Size:",
			[LanguageCode.Ja] = "中央フォントサイズ:"
		};
		dictionary["CoreTextColorAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动适应配色主题 (Auto Contrast)",
			[LanguageCode.ZhTw] = "自動適應配色主題 (Auto Contrast)",
			[LanguageCode.En] = "Auto Contrast",
			[LanguageCode.Ja] = "配色テーマに自動追従 (Auto Contrast)"
		};
		dictionary["CoreTextColor"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心文字颜色 (Core Text Color):",
			[LanguageCode.ZhTw] = "中心文字顏色 (Core Text Color):",
			[LanguageCode.En] = "Center Text Color:",
			[LanguageCode.Ja] = "中央文字色:"
		};
		dictionary["ClickSectorHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 提示：在右侧画布中点击任意扇区可直接选中",
			[LanguageCode.ZhTw] = "💡 提示：在右側畫布中點選任意扇區可直接選取",
			[LanguageCode.En] = "Tip: Click any sector on the canvas to select",
			[LanguageCode.Ja] = "ヒント: キャンバス上の扇形をクリックして直接選択"
		};
		dictionary["InheritGlobal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "跟随全局默认 (Inherit Global)",
			[LanguageCode.ZhTw] = "跟隨全域預設 (Inherit Global)",
			[LanguageCode.En] = "Inherit Global Default",
			[LanguageCode.Ja] = "グローバルデフォルトを継承"
		};
		dictionary["CoreTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "中心核圆图案定制 (Center Core Customization)",
			[LanguageCode.ZhTw] = "中心核圓圖案自訂 (Center Core Customization)",
			[LanguageCode.En] = "Center Core Customization",
			[LanguageCode.Ja] = "中央コアのカスタマイズ"
		};
		dictionary["CoreShowIcon"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "显示中心图案 / 贴图",
			[LanguageCode.ZhTw] = "顯示中心圖案 / 貼圖",
			[LanguageCode.En] = "Show Core Icon / Image",
			[LanguageCode.Ja] = "中央アイコン/画像を表示"
		};
		dictionary["CoreIconType"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "核圆图案模式",
			[LanguageCode.ZhTw] = "核圓圖案模式",
			[LanguageCode.En] = "Core Pattern Mode",
			[LanguageCode.Ja] = "コアパターンモード"
		};
		dictionary["CorePatternExit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "取消叉号 (Cancel Cross)",
			[LanguageCode.ZhTw] = "取消叉號 (Cancel Cross)",
			[LanguageCode.En] = "Cancel Cross",
			[LanguageCode.Ja] = "キャンセルバツ"
		};
		dictionary["CorePatternCrosshair"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "精准准心 (Crosshair)",
			[LanguageCode.ZhTw] = "精準準心 (Crosshair)",
			[LanguageCode.En] = "Crosshair",
			[LanguageCode.Ja] = "照準レティクル"
		};
		dictionary["CorePatternWindows"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Windows 微标",
			[LanguageCode.ZhTw] = "Windows 微標",
			[LanguageCode.En] = "Windows Emblem",
			[LanguageCode.Ja] = "Windows ロゴ"
		};
		dictionary["CorePatternDot"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简圆点 (Minimal Dot)",
			[LanguageCode.ZhTw] = "極簡圓點 (Minimal Dot)",
			[LanguageCode.En] = "Minimal Dot",
			[LanguageCode.Ja] = "ミニマルドット"
		};
		dictionary["CorePatternHome"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "主页图标 (Home)",
			[LanguageCode.ZhTw] = "首頁圖示 (Home)",
			[LanguageCode.En] = "Home",
			[LanguageCode.Ja] = "ホーム"
		};
		dictionary["CorePatternPower"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "电源图标 (Power)",
			[LanguageCode.ZhTw] = "電源圖示 (Power)",
			[LanguageCode.En] = "Power",
			[LanguageCode.Ja] = "電源"
		};
		dictionary["CorePatternCompass"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "星空罗盘 (Compass)",
			[LanguageCode.ZhTw] = "星空羅盤 (Compass)",
			[LanguageCode.En] = "Compass",
			[LanguageCode.Ja] = "コンパス"
		};
		dictionary["CorePatternCatPaw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "萌宠猫爪 (Cat Paw)",
			[LanguageCode.ZhTw] = "萌寵貓爪 (Cat Paw)",
			[LanguageCode.En] = "Cat Paw",
			[LanguageCode.Ja] = "肉球"
		};
		dictionary["CorePatternImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\uddbc\ufe0f 自定义本地图片贴图...",
			[LanguageCode.ZhTw] = "\ud83d\uddbc\ufe0f 自訂本機圖片貼圖...",
			[LanguageCode.En] = "\ud83d\uddbc\ufe0f Custom Local Image...",
			[LanguageCode.Ja] = "\ud83d\uddbc\ufe0f カスタム画像ファイル..."
		};
		dictionary["BtnBrowseImage"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "浏览选择图片",
			[LanguageCode.ZhTw] = "瀏覽選擇圖片",
			[LanguageCode.En] = "Browse Image",
			[LanguageCode.Ja] = "画像を選択"
		};
		dictionary["ConsoleThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "软件控制台主题 (Console Theme)",
			[LanguageCode.ZhTw] = "軟體控制台主題 (Console Theme)",
			[LanguageCode.En] = "Console Theme",
			[LanguageCode.Ja] = "コントロールパネルテーマ"
		};
		dictionary["ThemeCustom"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 自定义配色 (Custom Colors)",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 自定義配色 (Custom Colors)",
			[LanguageCode.En] = "\ud83c\udfa8 Custom Colors",
			[LanguageCode.Ja] = "\ud83c\udfa8 カスタム配色"
		};
		dictionary["ThemeSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udda5\ufe0f 跟随 Windows 系统 (Auto)",
			[LanguageCode.ZhTw] = "\ud83d\udda5\ufe0f 跟隨 Windows 系統 (Auto)",
			[LanguageCode.En] = "\ud83d\udda5\ufe0f Follow Windows System",
			[LanguageCode.Ja] = "\ud83d\udda5\ufe0f Windows システムに従う"
		};
		dictionary["ThemeLight"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "☀\ufe0f 极简纯白 (Pure Light)",
			[LanguageCode.ZhTw] = "☀\ufe0f 極簡純白 (Pure Light)",
			[LanguageCode.En] = "☀\ufe0f Pure Light",
			[LanguageCode.Ja] = "☀\ufe0f ピュアライト"
		};
		dictionary["ThemeDark"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf19 极夜曜黑 (Oled Dark)",
			[LanguageCode.ZhTw] = "\ud83c\udf19 極夜曜黑 (Oled Dark)",
			[LanguageCode.En] = "\ud83c\udf19 OLED Dark",
			[LanguageCode.Ja] = "\ud83c\udf19 OLEDダーク"
		};
		dictionary["ThemeNavy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf0c 午夜深蓝 (Midnight Navy)",
			[LanguageCode.ZhTw] = "\ud83c\udf0c 午夜深藍 (Midnight Navy)",
			[LanguageCode.En] = "\ud83c\udf0c Midnight Navy",
			[LanguageCode.Ja] = "\ud83c\udf0c ミッドナイトネイビー"
		};
		dictionary["ThemeViolet"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd2e 暗夜紫罗兰 (Royal Violet)",
			[LanguageCode.ZhTw] = "\ud83d\udd2e 暗夜紫羅蘭 (Royal Violet)",
			[LanguageCode.En] = "\ud83d\udd2e Royal Violet",
			[LanguageCode.Ja] = "\ud83d\udd2e ロイヤルバイオレット"
		};
		dictionary["ThemeGray"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 钛金深灰 (Titanium Gray)",
			[LanguageCode.ZhTw] = "⚙\ufe0f 鈦金深灰 (Titanium Gray)",
			[LanguageCode.En] = "⚙\ufe0f Titanium Gray",
			[LanguageCode.Ja] = "⚙\ufe0f チタングレー"
		};
		dictionary["GesturesHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手势轮盘分位与动作配置",
			[LanguageCode.ZhTw] = "手勢輪盤分位與動作配置",
			[LanguageCode.En] = "Gesture Sectors & Action Mappings",
			[LanguageCode.Ja] = "セクター配置とアクション設定"
		};
		dictionary["SectorCountTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘方位按键数 (Sector Count)",
			[LanguageCode.ZhTw] = "輪盤方位按鍵數 (Sector Count)",
			[LanguageCode.En] = "Sector Count",
			[LanguageCode.Ja] = "セクター数（キー数）"
		};
		dictionary["SectorCount4"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "4 键 (十字方位 / Cross 4-Way)",
			[LanguageCode.ZhTw] = "4 鍵 (十字方位 / Cross 4-Way)",
			[LanguageCode.En] = "4 Sectors (Cross 4-Way)",
			[LanguageCode.Ja] = "4キー (十字方向)"
		};
		dictionary["SectorCount8"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "8 键 (八卦全向 / Standard 8-Way)",
			[LanguageCode.ZhTw] = "8 鍵 (八卦全向 / Standard 8-Way)",
			[LanguageCode.En] = "8 Sectors (Standard 8-Way)",
			[LanguageCode.Ja] = "8キー (全方向8方位)"
		};
		dictionary["SectorCount12"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "12 键 (钟表表盘 / Clock Dial 12-Way)",
			[LanguageCode.ZhTw] = "12 鍵 (鐘錶錶盤 / Clock Dial 12-Way)",
			[LanguageCode.En] = "12 Sectors (Clock Dial 12-Way)",
			[LanguageCode.Ja] = "12キー (時計盤12方位)"
		};
		dictionary["ActionTypeHotkey"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⌨\ufe0f 键盘快捷键",
			[LanguageCode.ZhTw] = "⌨\ufe0f 鍵盤快速鍵",
			[LanguageCode.En] = "⌨\ufe0f Keyboard Hotkey",
			[LanguageCode.Ja] = "⌨\ufe0f キーボードショートカット"
		};
		dictionary["ActionTypeLaunch"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\ude80 启动程序/打开网页",
			[LanguageCode.ZhTw] = "\ud83d\ude80 啟動程式/開啟網頁",
			[LanguageCode.En] = "\ud83d\ude80 Launch App / Open URL",
			[LanguageCode.Ja] = "\ud83d\ude80 アプリ起動 / Webを開く"
		};
		dictionary["ActionTypeSystem"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 系统控制指令",
			[LanguageCode.ZhTw] = "⚙\ufe0f 系統控制指令",
			[LanguageCode.En] = "⚙\ufe0f System Action",
			[LanguageCode.Ja] = "⚙\ufe0f システム制御コマンド"
		};
		dictionary["BtnRecordHotkey"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "点击录制热键",
			[LanguageCode.ZhTw] = "點擊錄製快速鍵",
			[LanguageCode.En] = "Click to Record Hotkey",
			[LanguageCode.Ja] = "クリックしてショートカット録画"
		};
		dictionary["BtnBrowseApp"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd0d 选择应用程序...",
			[LanguageCode.ZhTw] = "\ud83d\udd0d 選擇應用程式...",
			[LanguageCode.En] = "\ud83d\udd0d Select Application...",
			[LanguageCode.Ja] = "\ud83d\udd0d アプリケーションを選択..."
		};
		dictionary["AdvancedHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统集成与高级偏好设置",
			[LanguageCode.ZhTw] = "系統整合與進階偏好設定",
			[LanguageCode.En] = "System Integration & Preferences",
			[LanguageCode.Ja] = "システム統合と高度な設定"
		};
		dictionary["LanguageTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "界面语言 (Display Language)",
			[LanguageCode.ZhTw] = "介面語言 (Display Language)",
			[LanguageCode.En] = "Display Language",
			[LanguageCode.Ja] = "表示言語 (Display Language)"
		};
		dictionary["LanguageDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择软件控制台与轮盘的显示语言，支持即时热切换并自动保存。",
			[LanguageCode.ZhTw] = "選擇軟體控制台與輪盤的顯示語言，支援即時熱切換並自動儲存。",
			[LanguageCode.En] = "Select language for StarPie. Applies immediately without restarting.",
			[LanguageCode.Ja] = "StarPieの表示言語を選択します。再起動不要で即時に切り替わります。"
		};
		dictionary["ProgramPickerTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择程序",
			[LanguageCode.ZhTw] = "選擇程式",
			[LanguageCode.En] = "Select Program",
			[LanguageCode.Ja] = "プログラムを選択"
		};
		dictionary["ProgramPickerHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "从已安装的软件和开始菜单中选择",
			[LanguageCode.ZhTw] = "從已安裝的軟體與開始功能表中選擇",
			[LanguageCode.En] = "Select from Installed Apps & Start Menu",
			[LanguageCode.Ja] = "インストール済みアプリやスタートメニューから選択"
		};
		dictionary["ProgramPickerPlaceholder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "搜索软件名称、可执行文件或路径...",
			[LanguageCode.ZhTw] = "搜尋軟體名稱、執行檔或路徑...",
			[LanguageCode.En] = "Search app name, executable, or path...",
			[LanguageCode.Ja] = "アプリ名、実行可能ファイル、またはパスを検索..."
		};
		dictionary["ProgramPickerScanning"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "正在智能检索系统中已安装的软件，请稍候...",
			[LanguageCode.ZhTw] = "正在智慧檢索系統中已安裝的軟體，請稍候...",
			[LanguageCode.En] = "Scanning installed programs, please wait...",
			[LanguageCode.Ja] = "インストール済みアプリをスキャンしています..."
		};
		dictionary["BtnManualBrowse"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "手动浏览文件...",
			[LanguageCode.ZhTw] = "手動瀏覽檔案...",
			[LanguageCode.En] = "Browse File...",
			[LanguageCode.Ja] = "手動で参照..."
		};
		dictionary["LangZhCn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)",
			[LanguageCode.ZhTw] = "\ud83c\udde8\ud83c\uddf3 簡體中文 (Simplified Chinese)",
			[LanguageCode.En] = "\ud83c\udde8\ud83c\uddf3 简体中文 (Simplified Chinese)",
			[LanguageCode.Ja] = "\ud83c\udde8\ud83c\uddf3 簡体字中国語 (Simplified Chinese)"
		};
		dictionary["LangZhTw"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.ZhTw] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.En] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁體中文 (Traditional Chinese)",
			[LanguageCode.Ja] = "\ud83c\udded\ud83c\uddf0/\ud83c\uddf9\ud83c\uddfc 繁体字中国語 (Traditional Chinese)"
		};
		dictionary["LangEn"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.ZhTw] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.En] = "\ud83c\uddfa\ud83c\uddf8 English (US/UK)",
			[LanguageCode.Ja] = "\ud83c\uddfa\ud83c\uddf8 英語 (English)"
		};
		dictionary["LangJa"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.ZhTw] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.En] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)",
			[LanguageCode.Ja] = "\ud83c\uddef\ud83c\uddf5 日本語 (Japanese)"
		};
		dictionary["LangAuto"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udda5\ufe0f 跟随系统 (System Default)",
			[LanguageCode.ZhTw] = "\ud83d\udda5\ufe0f 跟隨系統 (System Default)",
			[LanguageCode.En] = "\ud83d\udda5\ufe0f System Default",
			[LanguageCode.Ja] = "\ud83d\udda5\ufe0f システム既定 (System Default)"
		};
		dictionary["StartupTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开机自启动",
			[LanguageCode.ZhTw] = "開機自啟動",
			[LanguageCode.En] = "Run on Windows Startup",
			[LanguageCode.Ja] = "Windows起動時に自動起動"
		};
		dictionary["StartupDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在 Windows 开机登录时静默自启动并在后台托盘驻留。",
			[LanguageCode.ZhTw] = "在 Windows 開機登入時靜默自啟動並在後台托盤駐留。",
			[LanguageCode.En] = "Automatically start StarPie silently minimized to tray on login.",
			[LanguageCode.Ja] = "Windows起動時に自動でタスクトレイに常駐します。"
		};
		dictionary["AutoStartAsAdminTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员权限自启动 (推荐)",
			[LanguageCode.ZhTw] = "以系統管理員權限自啟動 (推薦)",
			[LanguageCode.En] = "Run with Administrator Privileges on Startup",
			[LanguageCode.Ja] = "管理者権限で自動起動 (推奨)"
		};
		dictionary["AutoStartAsAdminDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "通过 Windows 任务计划程序以最高权限静默自启，无需每次弹出 UAC，可在各类高权限窗口中正常响应手势。",
			[LanguageCode.ZhTw] = "透過 Windows 工作排程器以最高權限靜默自啟，無需每次彈出 UAC，可在各類高權限視窗中正常回應手勢。",
			[LanguageCode.En] = "Launches via Windows Task Scheduler with highest privileges without UAC prompt, ensuring gestures work in elevated windows.",
			[LanguageCode.Ja] = "Windowsタスクスケジューラを利用してUACなしで最高権限で自動起動し、管理者権限ウィンドウでも動作します。"
		};
		dictionary["ProgramPickerRefresh"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd04 刷新列表",
			[LanguageCode.ZhTw] = "\ud83d\udd04 重新整理",
			[LanguageCode.En] = "\ud83d\udd04 Refresh",
			[LanguageCode.Ja] = "\ud83d\udd04 更新"
		};
		dictionary["Tier1ConfigSegment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udd18 一级主轮盘配置",
			[LanguageCode.ZhTw] = "\ud83d\udd18 一級主輪盤配置",
			[LanguageCode.En] = "\ud83d\udd18 Tier 1 Primary Wheel",
			[LanguageCode.Ja] = "\ud83d\udd18 第1層メインホイール"
		};
		dictionary["Tier2ConfigSegment"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udf1f 二级级联轮盘配置",
			[LanguageCode.ZhTw] = "\ud83c\udf1f 二級級聯輪盤配置",
			[LanguageCode.En] = "\ud83c\udf1f Tier 2 Sub-Wheel",
			[LanguageCode.Ja] = "\ud83c\udf1f 第2層カスケードホイール"
		};
		dictionary["SubWheelThemeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "二级轮盘视觉风格与色彩配置",
			[LanguageCode.ZhTw] = "二級輪盤視覺風格與色彩配置",
			[LanguageCode.En] = "Tier 2 Visual Style & Color Scheme",
			[LanguageCode.Ja] = "第2層ホイールのスタイルと配色"
		};
		dictionary["SubWheelThemeDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "为二级级联轮盘独立指定视觉渲染器与主题配色，与一级主轮盘自由组合。",
			[LanguageCode.ZhTw] = "為二級級聯輪盤獨立指定視覺渲染器與主題配色，與一級主輪盤自由組合。",
			[LanguageCode.En] = "Independently customize visual style and colors for the secondary cascading wheel.",
			[LanguageCode.Ja] = "第2層カスケードホイールに独自のスタイルと配色を設定します。"
		};
		dictionary["MemoryTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "极简内存优化 (Working Set Trim)",
			[LanguageCode.ZhTw] = "極簡記憶體最佳化 (Working Set Trim)",
			[LanguageCode.En] = "Memory Optimization",
			[LanguageCode.Ja] = "メモリ最適化 (ワーキングセット圧縮)"
		};
		dictionary["MemoryDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用 Windows 进程工作集深度修剪，后台常驻内存低至 15~25MB。",
			[LanguageCode.ZhTw] = "啟用 Windows 行程工作集深度修剪，後台常駐記憶體低至 15~25MB。",
			[LanguageCode.En] = "Deep trims working set, keeping background RAM usage under 20MB.",
			[LanguageCode.Ja] = "メモリを自動トリムし、バックグラウンド使用量を15〜25MBに維持します。"
		};
		dictionary["BtnTrimMemory"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "立即压缩物理内存",
			[LanguageCode.ZhTw] = "立即壓縮實體記憶體",
			[LanguageCode.En] = "Trim RAM Now",
			[LanguageCode.Ja] = "今すぐメモリ圧縮"
		};
		dictionary["ElevateTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "管理员权限提升 (Run as Admin)",
			[LanguageCode.ZhTw] = "系統管理員權限提升 (Run as Admin)",
			[LanguageCode.En] = "Run as Administrator",
			[LanguageCode.Ja] = "管理者権限で実行"
		};
		dictionary["ElevateDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员身份重启，可在任务管理器、系统设置等高权限窗口中正常唤起手势。",
			[LanguageCode.ZhTw] = "以系統管理員身分重啟，可在工作管理員、系統設定等高權限視窗中正常呼出手勢。",
			[LanguageCode.En] = "Relaunch with administrator privileges to interact with elevated windows.",
			[LanguageCode.Ja] = "管理者権限で再起動し、タスクマネージャー等の高権限画面でも動作可能にします。"
		};
		dictionary["BtnElevate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 以管理员身份重启",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 以系統管理員身分重啟",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Restart as Administrator",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f 管理者として再起動"
		};
		dictionary["BackupTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "配置备份与恢复 (Backup & Reset)",
			[LanguageCode.ZhTw] = "配置備份與恢復 (Backup & Reset)",
			[LanguageCode.En] = "Backup & Reset",
			[LanguageCode.Ja] = "バックアップとリセット"
		};
		dictionary["BtnExportConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "导出配置备份",
			[LanguageCode.ZhTw] = "匯出配置備份",
			[LanguageCode.En] = "Export Backup",
			[LanguageCode.Ja] = "設定をエクスポート"
		};
		dictionary["BtnImportConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "导入配置文件",
			[LanguageCode.ZhTw] = "匯入設定檔",
			[LanguageCode.En] = "Import Config",
			[LanguageCode.Ja] = "設定をインポート"
		};
		dictionary["BtnResetConfig"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "恢复出厂设置",
			[LanguageCode.ZhTw] = "恢復原廠設定",
			[LanguageCode.En] = "Restore Factory Defaults",
			[LanguageCode.Ja] = "初期設定にリセット"
		};
		dictionary["LogsTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "系统运行日志与诊断 (Runtime Logs)",
			[LanguageCode.ZhTw] = "系統運行日誌與診斷 (Runtime Logs)",
			[LanguageCode.En] = "System Runtime Logs & Diagnostics",
			[LanguageCode.Ja] = "システム動作ログと診断 (Runtime Logs)"
		};
		dictionary["LogsDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "自动记录系统生命周期、手势分发、按键模拟与故障异常信息（保留最近7天），方便故障排查与问题反馈。",
			[LanguageCode.ZhTw] = "自動記錄系統生命週期、手勢分發、按鍵模擬與故障異常資訊（保留最近7天），方便故障排查與問題回饋。",
			[LanguageCode.En] = "Automatically records system lifecycle, gesture events, key simulations, and exceptions (retains 7 days) for diagnostics.",
			[LanguageCode.Ja] = "システムのライフサイクル、ジェスチャイベント、キーシミュレーション、例外を自動記録します（過去7日間保持）。"
		};
		dictionary["BtnOpenLogFolder"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc2 打开日志目录",
			[LanguageCode.ZhTw] = "\ud83d\udcc2 開啟日誌目錄",
			[LanguageCode.En] = "\ud83d\udcc2 Open Log Folder",
			[LanguageCode.Ja] = "\ud83d\udcc2 ログフォルダーを開く"
		};
		dictionary["BtnViewTodayLog"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udcc4 查看今日运行日志",
			[LanguageCode.ZhTw] = "\ud83d\udcc4 檢視今日運行日誌",
			[LanguageCode.En] = "\ud83d\udcc4 View Today's Log",
			[LanguageCode.Ja] = "\ud83d\udcc4 今日のログを表示"
		};
		dictionary["AboutHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "关于 StarPie & 版本记录",
			[LanguageCode.ZhTw] = "關於 StarPie & 版本記錄",
			[LanguageCode.En] = "About StarPie & Changelog",
			[LanguageCode.Ja] = "StarPie について & 更新履歴"
		};
		dictionary["AboutDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高质感、极速现代 Windows 鼠标轮盘笔势工具",
			[LanguageCode.ZhTw] = "高質感、極速現代 Windows 滑鼠輪盤手勢工具",
			[LanguageCode.En] = "High-aesthetic, ultra-fast modern Windows mouse radial gestures tool.",
			[LanguageCode.Ja] = "洗練されたデザインと高速な応答性を誇る次世代マウスジェスチャーツール"
		};
		dictionary["BtnOpenChangelog"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "查看完整 CHANGELOG",
			[LanguageCode.ZhTw] = "檢視完整 CHANGELOG",
			[LanguageCode.En] = "View Full CHANGELOG",
			[LanguageCode.Ja] = "完全な更新履歴を表示"
		};
		dictionary["MilestonesTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "版本演进里程碑 (Milestones)",
			[LanguageCode.ZhTw] = "版本演進里程碑 (Milestones)",
			[LanguageCode.En] = "Version Milestones",
			[LanguageCode.Ja] = "バージョン履歴"
		};
		dictionary["MsgSaveSuccess"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "设置已成功保存至硬盘！",
			[LanguageCode.ZhTw] = "設定已成功儲存至硬碟！",
			[LanguageCode.En] = "Settings successfully saved to disk!",
			[LanguageCode.Ja] = "設定が正常に保存されました！"
		};
		dictionary["MsgConfirmDeletePreset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要永久删除此自定义配色方案吗？\n删除后不可恢复。",
			[LanguageCode.ZhTw] = "確定要永久刪除此自訂配色方案嗎？\n刪除後不可恢復。",
			[LanguageCode.En] = "Are you sure you want to delete this custom color preset?\nThis cannot be undone.",
			[LanguageCode.Ja] = "このカスタム配色プリセットを削除してもよろしいですか？\n削除後は復元できません。"
		};
		dictionary["MsgConfirmReset"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要恢复出厂默认设置吗？所有自定义手势与样式将被重置。",
			[LanguageCode.ZhTw] = "確定要恢復原廠預設設定嗎？所有自訂手勢與樣式將被重設。",
			[LanguageCode.En] = "Are you sure you want to restore factory defaults? All customizations will be reset.",
			[LanguageCode.Ja] = "工場出荷時の初期設定に戻してもよろしいですか？すべてのカスタム設定がリセットされます。"
		};
		dictionary["TrayPause"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏸\ufe0f 暂停手势",
			[LanguageCode.ZhTw] = "⏸\ufe0f 暫停手勢",
			[LanguageCode.En] = "⏸\ufe0f Pause Gestures",
			[LanguageCode.Ja] = "⏸\ufe0f ジェスチャーを一時停止"
		};
		dictionary["TrayResume"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "▶\ufe0f 恢复手势",
			[LanguageCode.ZhTw] = "▶\ufe0f 恢復手勢",
			[LanguageCode.En] = "▶\ufe0f Resume Gestures",
			[LanguageCode.Ja] = "▶\ufe0f ジェスチャーを再開"
		};
		dictionary["TrayPreferences"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙\ufe0f 偏好设置 (Settings)",
			[LanguageCode.ZhTw] = "⚙\ufe0f 偏好設定 (Settings)",
			[LanguageCode.En] = "⚙\ufe0f Preferences (Settings)",
			[LanguageCode.Ja] = "⚙\ufe0f 環境設定 (Settings)"
		};
		dictionary["TrayAppearance"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83c\udfa8 外观与形态 (Appearance)",
			[LanguageCode.ZhTw] = "\ud83c\udfa8 外觀與形態 (Appearance)",
			[LanguageCode.En] = "\ud83c\udfa8 Appearance & Shapes",
			[LanguageCode.Ja] = "\ud83c\udfa8 外観と形状"
		};
		dictionary["TrayGestures"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 手势与动作 (Mappings)",
			[LanguageCode.ZhTw] = "⚡ 手勢與動作 (Mappings)",
			[LanguageCode.En] = "⚡ Gestures & Actions",
			[LanguageCode.Ja] = "⚡ ジェスチャーと動作"
		};
		dictionary["TrayAbout"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udccb 更新日志与关于 (About)",
			[LanguageCode.ZhTw] = "\ud83d\udccb 更新日誌與關於 (About)",
			[LanguageCode.En] = "\ud83d\udccb About & Changelog",
			[LanguageCode.Ja] = "\ud83d\udccb 情報と更新履歴"
		};
		dictionary["TrayElevate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "\ud83d\udee1\ufe0f 以管理员身份重启",
			[LanguageCode.ZhTw] = "\ud83d\udee1\ufe0f 以系統管理員身分重啟",
			[LanguageCode.En] = "\ud83d\udee1\ufe0f Restart as Administrator",
			[LanguageCode.Ja] = "\ud83d\udee1\ufe0f 管理者として再起動"
		};
		dictionary["TrayExit"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "❌ 退出 StarPie",
			[LanguageCode.ZhTw] = "❌ 退出 StarPie",
			[LanguageCode.En] = "❌ Exit StarPie",
			[LanguageCode.Ja] = "❌ StarPie を終了"
		};
		dictionary["TrayTooltip"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "StarPie - 现代化鼠标轮盘笔势",
			[LanguageCode.ZhTw] = "StarPie - 現代化滑鼠輪盤手勢",
			[LanguageCode.En] = "StarPie - Modern Mouse Radial Gestures",
			[LanguageCode.Ja] = "StarPie - 次世代マウスラジアルジェスチャー"
		};
		dictionary["UpdateSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "软件更新与版本管理 (Software Updates)",
			[LanguageCode.ZhTw] = "軟體更新與版本管理 (Software Updates)",
			[LanguageCode.En] = "Software Updates & Version Control",
			[LanguageCode.Ja] = "ソフトウェア更新とバージョン管理"
		};
		dictionary["UpdateStatusLatest"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已是最新版本",
			[LanguageCode.ZhTw] = "已是最新版本",
			[LanguageCode.En] = "Up to Date",
			[LanguageCode.Ja] = "最新バージョンです"
		};
		dictionary["UpdateStatusNewVersion"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "发现新版本",
			[LanguageCode.ZhTw] = "發現新版本",
			[LanguageCode.En] = "Update Available",
			[LanguageCode.Ja] = "新しいバージョンがあります"
		};
		dictionary["BtnCheckUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 立即检查更新",
			[LanguageCode.ZhTw] = "🔄 立即檢查更新",
			[LanguageCode.En] = "🔄 Check Updates",
			[LanguageCode.Ja] = "🔄 更新を確認"
		};
		dictionary["BtnCheckingUpdate"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⏳ 正在检查...",
			[LanguageCode.ZhTw] = "⏳ 正在檢查...",
			[LanguageCode.En] = "⏳ Checking...",
			[LanguageCode.Ja] = "⏳ 確認中..."
		};
		dictionary["UpdateSilentCheckTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开机静默检查更新",
			[LanguageCode.ZhTw] = "開機靜默檢查更新",
			[LanguageCode.En] = "Silent Check on Startup",
			[LanguageCode.Ja] = "起動時のサイレント更新確認"
		};
		dictionary["UpdateSilentCheckDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "程序启动后在后台静默查询 GitHub Releases",
			[LanguageCode.ZhTw] = "程式啟動後在後台靜默查詢 GitHub Releases",
			[LanguageCode.En] = "Silently query GitHub Releases in the background after launch",
			[LanguageCode.Ja] = "起動後にバックグラウンドで GitHub Releases をサイレント確認"
		};
		dictionary["UpdateChannelTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "更新推送通道",
			[LanguageCode.ZhTw] = "更新推送通道",
			[LanguageCode.En] = "Update Channel",
			[LanguageCode.Ja] = "更新チャンネル"
		};
		dictionary["UpdateChannelDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "选择稳定版或尝鲜版",
			[LanguageCode.ZhTw] = "選擇穩定版或嘗鮮版",
			[LanguageCode.En] = "Select Stable or Preview releases",
			[LanguageCode.Ja] = "安定版またはプレビュー版を選択"
		};
		dictionary["UpdateChannelStable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 正式稳定版",
			[LanguageCode.ZhTw] = "🌟 正式穩定版",
			[LanguageCode.En] = "🌟 Stable Release",
			[LanguageCode.Ja] = "🌟 安定版"
		};
		dictionary["UpdateChannelBeta"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚀 尝鲜测试版",
			[LanguageCode.ZhTw] = "🚀 嘗鮮測試版",
			[LanguageCode.En] = "🚀 Preview / Beta",
			[LanguageCode.Ja] = "🚀 プレビュー版"
		};
		dictionary["UpdateProxyTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "GitHub 下载加速镜像源",
			[LanguageCode.ZhTw] = "GitHub 下載加速鏡像源",
			[LanguageCode.En] = "GitHub Download Mirror",
			[LanguageCode.Ja] = "GitHub ダウンロードミラー"
		};
		dictionary["UpdateProxyDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "解决国内访问 GitHub Releases 丢包或限速问题，自动加速代理下载",
			[LanguageCode.ZhTw] = "解決訪問 GitHub Releases 封包遺失或限速問題，自動加速代理下載",
			[LanguageCode.En] = "Accelerate GitHub Releases download and mitigate connection issues",
			[LanguageCode.Ja] = "GitHub Releases への接続を高速化し、プロキシ経由でダウンロード"
		};
		dictionary["UpdateProxyDirect"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 官方直连 (GitHub Official)",
			[LanguageCode.ZhTw] = "🌐 官方直連 (GitHub Official)",
			[LanguageCode.En] = "🌐 Official Direct (GitHub)",
			[LanguageCode.Ja] = "🌐 公式ダイレクト (GitHub)"
		};
		dictionary["UpdateProxyGhproxy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 1 (ghfast.top 推荐)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 1 (ghfast.top 推薦)",
			[LanguageCode.En] = "⚡ Mirror 1 (ghfast.top Recommended)",
			[LanguageCode.Ja] = "⚡ ミラー 1 (ghfast.top 推奨)"
		};
		dictionary["UpdateProxyMoeyy"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 2 (gh-proxy.com 备用)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 2 (gh-proxy.com 備用)",
			[LanguageCode.En] = "⚡ Mirror 2 (gh-proxy.com Backup)",
			[LanguageCode.Ja] = "⚡ ミラー 2 (gh-proxy.com 予備)"
		};
		dictionary["UpdateProxyAkams"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚡ 镜像源 3 (mirror.ghproxy.com)",
			[LanguageCode.ZhTw] = "⚡ 鏡像源 3 (mirror.ghproxy.com)",
			[LanguageCode.En] = "⚡ Mirror 3 (mirror.ghproxy.com)",
			[LanguageCode.Ja] = "⚡ ミラー 3 (mirror.ghproxy.com)"
		};
		dictionary["RollbackSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "历史版本回退 (Version Rollback)",
			[LanguageCode.ZhTw] = "歷史版本回退 (Version Rollback)",
			[LanguageCode.En] = "Version Rollback",
			[LanguageCode.Ja] = "過去バージョンへのロールバック"
		};
		dictionary["RollbackSectionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "若当前版本发生兼容性或配置异常，可选择历史版本一键覆盖回退安装。",
			[LanguageCode.ZhTw] = "若當前版本發生相容性或設定異常，可選擇歷史版本一鍵覆蓋回退安裝。",
			[LanguageCode.En] = "If compatibility issues occur, rollback to a prior version with one click.",
			[LanguageCode.Ja] = "互換性の問題が発生した場合は、ワンクリックで以前のバージョンにロールバックできます。"
		};
		dictionary["RollbackBadgeBeta"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🚀 测试版最多回退5个版本",
			[LanguageCode.ZhTw] = "🚀 測試版最多回退5個版本",
			[LanguageCode.En] = "🚀 Beta track: up to 5 versions",
			[LanguageCode.Ja] = "🚀 ベータ版：最大5バージョンまでロールバック可能"
		};
		dictionary["RollbackBadgeStable"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌟 正式版最多回退2个版本",
			[LanguageCode.ZhTw] = "🌟 正式版最多回退2個版本",
			[LanguageCode.En] = "🌟 Stable track: up to 2 versions",
			[LanguageCode.Ja] = "🌟 安定版：最大2バージョンまでロールバック可能"
		};
		dictionary["BtnRollback"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⬇️ 回退至此版本",
			[LanguageCode.ZhTw] = "⬇️ 回退至此版本",
			[LanguageCode.En] = "⬇️ Rollback to this version",
			[LanguageCode.Ja] = "⬇️ このバージョンにロールバック"
		};
		dictionary["RollbackEmpty"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "（暂无可回退历史版本）",
			[LanguageCode.ZhTw] = "（暫無可回退歷史版本）",
			[LanguageCode.En] = "(No rollback versions available)",
			[LanguageCode.Ja] = "（ロールバック可能なバージョンはありません）"
		};
		dictionary["RollbackConfirmTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确认版本回退",
			[LanguageCode.ZhTw] = "確認版本回退",
			[LanguageCode.En] = "Confirm Version Rollback",
			[LanguageCode.Ja] = "ロールバックの確認"
		};
		dictionary["RollbackConfirmMsg"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "确定要将 StarPie 回退至版本 {0} 吗？\n\n程序将下载该历史版本安装包并自动覆盖重启。您的自定义手势与按键配置将完整保留。",
			[LanguageCode.ZhTw] = "確定要將 StarPie 回退至版本 {0} 嗎？\n\n程式將下載該歷史版本安裝包並自動覆蓋重啟。您的自訂手勢與按鍵設定將完整保留。",
			[LanguageCode.En] = "Are you sure you want to rollback StarPie to version {0}?\n\nThe update package will be downloaded and safely applied upon restart. Your configurations will be preserved.",
			[LanguageCode.Ja] = "StarPie をバージョン {0} にロールバックしてもよろしいですか？\n\nパッケージをダウンロードして再起動時に適用されます。現在の設定は保持されます。"
		};
		dictionary["ContributorsHeader"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "开源贡献者致谢",
			[LanguageCode.ZhTw] = "開源貢獻者致謝",
			[LanguageCode.En] = "Contributors & Thanks",
			[LanguageCode.Ja] = "コントリビューターへの感謝"
		};
		dictionary["ContributorsIntro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "感谢以下贡献者为 StarPie (星盘) 开源项目付出的智慧与贡献：",
			[LanguageCode.ZhTw] = "感謝以下貢獻者為 StarPie (星盤) 開源項目付出的智慧與貢獻：",
			[LanguageCode.En] = "Thank you to all contributors who empower StarPie open-source project:",
			[LanguageCode.Ja] = "StarPie オープンソースプロジェクトに貢献してくださった皆様に感謝いたします:"
		};
		dictionary["ContributorsSyncLocal"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🌐 本地收录名单 (检查更新时可联网刷新)",
			[LanguageCode.ZhTw] = "🌐 本地收錄名單 (檢查更新時可連網重新整理)",
			[LanguageCode.En] = "🌐 Local roster (refreshed when checking updates)",
			[LanguageCode.Ja] = "🌐 ローカル収録名簿 (更新確認時にオンライン更新)"
		};
		dictionary["ContributorsRefresh"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 刷新",
			[LanguageCode.ZhTw] = "🔄 重新整理",
			[LanguageCode.En] = "🔄 Refresh",
			[LanguageCode.Ja] = "🔄 更新"
		};
		dictionary["ContributorsRepo"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "★ 访问 GitHub 仓库",
			[LanguageCode.ZhTw] = "★ 造訪 GitHub 倉庫",
			[LanguageCode.En] = "★ Visit GitHub Repo",
			[LanguageCode.Ja] = "★ GitHub リポジトリへ"
		};
		dictionary["OcrCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "OCR 截屏文字识别与智能接口配置",
			[LanguageCode.ZhTw] = "OCR 截圖文字辨識與智慧介面配置",
			[LanguageCode.En] = "OCR Text Recognition & AI Model Setup",
			[LanguageCode.Ja] = "OCR 画面文字認識と AI モデル設定"
		};
		dictionary["OcrCardDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "管理原生离线识别引擎、多模态 AI 视觉模型 (OpenAI / 硅基流动 / Ollama) 与私有化 HTTP 接口端点与凭证。",
			[LanguageCode.ZhTw] = "管理原生離線辨識引擎、多模態 AI 視覺模型 (OpenAI / 矽基流動 / Ollama) 與私有化 HTTP 介面端點與憑證。",
			[LanguageCode.En] = "Manage native offline OCR engines, multimodal AI visual models, and custom HTTP endpoints.",
			[LanguageCode.Ja] = "ネイティブオフラインOCRエンジン、マルチモーダルAI、およびカスタムHTTPエンドポイントを管理。"
		};
		dictionary["OcrBadgeLocalEngine"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "Windows 本地离线引擎",
			[LanguageCode.ZhTw] = "Windows 本地離線引擎",
			[LanguageCode.En] = "Windows Local OCR",
			[LanguageCode.Ja] = "Windows ローカルOCR"
		};
		dictionary["BtnTestOcr"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "✂️ 截屏测试",
			[LanguageCode.ZhTw] = "✂️ 截圖測試",
			[LanguageCode.En] = "✂️ Test Snipping",
			[LanguageCode.Ja] = "✂️ 認識テスト"
		};
		dictionary["BtnConfigOcr"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 配置接口与模型...",
			[LanguageCode.ZhTw] = "⚙️ 配置介面與模型...",
			[LanguageCode.En] = "⚙️ Setup Engine & API...",
			[LanguageCode.Ja] = "⚙️ エンジンとAPI設定..."
		};
		dictionary["AutoStartAsAdminTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "以管理员身份开机自启 (推荐)",
			[LanguageCode.ZhTw] = "以系統管理員身分開機自啟 (推薦)",
			[LanguageCode.En] = "Run as Administrator on Startup (Recommended)",
			[LanguageCode.Ja] = "管理者として自動起動 (推奨)"
		};
		dictionary["AutoStartAsAdminDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "通过 Windows 任务计划程序实现最高权限静默自启，无需每次弹出 UAC 提示，可对所有高权限应用生效。",
			[LanguageCode.ZhTw] = "透過 Windows 工作排程器實現最高權限靜默自啟，無需每次跳出 UAC 提示，可對所有高權限應用程式生效。",
			[LanguageCode.En] = "Launch silently with elevated privileges via Windows Task Scheduler without UAC prompts, working across all admin apps.",
			[LanguageCode.Ja] = "Windows タスクスケジューラ経由でUACプロンプトなしに昇格起動し、管理者権限アプリでも確実に機能します。"
		};
		dictionary["CoreGlobalActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "全局动作",
			[LanguageCode.ZhTw] = "全域動作",
			[LanguageCode.En] = "Global Actions",
			[LanguageCode.Ja] = "グローバルアクション"
		};
		dictionary["CoreSectorActions"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "{0} 键动作",
			[LanguageCode.ZhTw] = "{0} 鍵動作",
			[LanguageCode.En] = "{0}-Slot Actions",
			[LanguageCode.Ja] = "{0}キー動作"
		};
		dictionary["DimensionsCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘几何尺寸与间距",
			[LanguageCode.ZhTw] = "輪盤幾何尺寸與間距",
			[LanguageCode.En] = "Radial Geometry & Spacing",
			[LanguageCode.Ja] = "ラジアルの幾何学的寸法と間隔"
		};
		dictionary["VisualThemeCardTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "切削形态与主题配色",
			[LanguageCode.ZhTw] = "切削形態與主題配色",
			[LanguageCode.En] = "Cutout Shapes & Color Themes",
			[LanguageCode.Ja] = "カット形状とカラーテーマ"
		};
		dictionary["ClickSectorHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 点击任意扇区可直接选中并在左侧微调该扇区的独立文字/图标排版",
			[LanguageCode.ZhTw] = "💡 點選任一扇區可直接選取並在左側微調該扇區的獨立文字/圖示排版",
			[LanguageCode.En] = "💡 Click any sector to select and customize its independent font/icon layout",
			[LanguageCode.Ja] = "💡 セクターをクリックして、フォントやアイコンの個別レイアウトを微調整できます"
		};
		dictionary["PreviewPanHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "按住 Ctrl + 鼠标拖动可平移画布",
			[LanguageCode.ZhTw] = "按住 Ctrl + 滑鼠拖曳可平移畫布",
			[LanguageCode.En] = "Hold Ctrl + Drag to pan canvas",
			[LanguageCode.Ja] = "Ctrl + ドラッグでキャンバスをパン"
		};
		dictionary["ConfigModeSimpleRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡單模式",
			[LanguageCode.En] = "💡 Simple Mode",
			[LanguageCode.Ja] = "💡 シンプルモード"
		};
		dictionary["ConfigModeProRadio"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级全量模式",
			[LanguageCode.ZhTw] = "⚙️ 高級全量模式",
			[LanguageCode.En] = "⚙️ Pro Full Mode",
			[LanguageCode.Ja] = "⚙️ プロ全機能モード"
		};
		dictionary["ConfigModeSimpleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "简单模式 (精简聚焦)",
			[LanguageCode.ZhTw] = "簡單模式 (精簡聚焦)",
			[LanguageCode.En] = "Simple Mode (Focused)",
			[LanguageCode.Ja] = "シンプルモード (集中)"
		};
		dictionary["ConfigModeSimpleHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已隐藏低频高级微调参数，保留核心极速配置体验",
			[LanguageCode.ZhTw] = "已隱藏低頻高級微調參數，保留核心極速設定體驗",
			[LanguageCode.En] = "Hidden low-frequency advanced parameters for a clean and focused experience",
			[LanguageCode.Ja] = "高度な設定項目を非表示にし、主要な設定に集中します"
		};
		dictionary["ConfigModeProTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "高级全量模式 (全开)",
			[LanguageCode.ZhTw] = "高級全量模式 (全開)",
			[LanguageCode.En] = "Pro Full Mode (Unrestricted)",
			[LanguageCode.Ja] = "プロ全機能モード (全開)"
		};
		dictionary["ConfigModeProHint"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "已全量开放运行命令、窗口管理、字体排版、平铺高级参数、OCR接口与系统日志等专家功能",
			[LanguageCode.ZhTw] = "已全量開放運行命令、視窗管理、字型排版、平鋪進階參數、OCR介面與系統日誌等專家功能",
			[LanguageCode.En] = "Full access to commands, window management, fonts, tiling parameters, OCR APIs and runtime logs",
			[LanguageCode.Ja] = "コマンド実行、ウィンドウ管理、フォント、分割詳細、OCR API、システムログなどの全機能を利用できます"
		};
		dictionary["SidebarModeSimple"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "💡 简单模式",
			[LanguageCode.ZhTw] = "💡 簡單模式",
			[LanguageCode.En] = "💡 Simple",
			[LanguageCode.Ja] = "💡 シンプル"
		};
		dictionary["SidebarModePro"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "⚙️ 高级模式",
			[LanguageCode.ZhTw] = "⚙️ 高級模式",
			[LanguageCode.En] = "⚙️ Pro Mode",
			[LanguageCode.Ja] = "⚙️ プロ"
		};
		dictionary["LayerIndicatorSectionTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "轮盘层数切换提示微标",
			[LanguageCode.ZhTw] = "輪盤層數切換提示微標",
			[LanguageCode.En] = "Layer Switch Indicator Badge",
			[LanguageCode.Ja] = "レイヤ切替インジケーター"
		};
		dictionary["LayerIndicatorSectionDesc"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "在高级全量模式下呼出轮盘后，滑动滚轮或按快捷键切换多层轮盘时的浮动提示徽标外观与停留时长。",
			[LanguageCode.ZhTw] = "在高級全量模式下呼出輪盤後，滑動滾輪或按快捷鍵切換多層輪盤時的浮動提示微標外觀與停留時長。",
			[LanguageCode.En] = "Appearance and fadeout duration of the floating layer badge when switching layers in Advanced Mode.",
			[LanguageCode.Ja] = "アドバンスモードでレイヤを切り替える際のフロートバッジの外観と表示時間。"
		};
		dictionary["ShowLayerIndicator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "启用层数切换浮动提示徽标 (Show Layer Indicator)",
			[LanguageCode.ZhTw] = "啟用層數切換浮動提示微標 (Show Layer Indicator)",
			[LanguageCode.En] = "Show Layer Switch Floating Badge",
			[LanguageCode.Ja] = "レイヤ切替フロートバッジを表示"
		};
		dictionary["LayerIndicatorStyleTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "徽标预设风格 (Preset Style):",
			[LanguageCode.ZhTw] = "微標預設風格 (Preset Style):",
			[LanguageCode.En] = "Preset Style:",
			[LanguageCode.Ja] = "プリセットスタイル:"
		};
		dictionary["LayerIndicatorIconTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示前置图标 (Indicator Icon):",
			[LanguageCode.ZhTw] = "提示前置圖示 (Indicator Icon):",
			[LanguageCode.En] = "Indicator Icon:",
			[LanguageCode.Ja] = "インジケーターアイコン:"
		};
		dictionary["LayerIndicatorCornerRadiusTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "徽标平滑圆角 (Corner Radius):",
			[LanguageCode.ZhTw] = "微標平滑圓角 (Corner Radius):",
			[LanguageCode.En] = "Badge Corner Radius:",
			[LanguageCode.Ja] = "バッジ角の丸み:"
		};
		dictionary["LayerIndicatorFontSizeTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "文字字号大小 (Font Size):",
			[LanguageCode.ZhTw] = "文字字型大小 (Font Size):",
			[LanguageCode.En] = "Font Size:",
			[LanguageCode.Ja] = "フォントサイズ:"
		};
		dictionary["LayerIndicatorOffsetYTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "垂直显示位置偏移 (Offset Y):",
			[LanguageCode.ZhTw] = "垂直顯示位置偏移 (Offset Y):",
			[LanguageCode.En] = "Vertical Offset Y:",
			[LanguageCode.Ja] = "垂直オフセット Y:"
		};
		dictionary["LayerIndicatorDurationTitle"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "提示淡出停留时间 (Duration):",
			[LanguageCode.ZhTw] = "提示淡出停留時間 (Duration):",
			[LanguageCode.En] = "Fadeout Duration:",
			[LanguageCode.Ja] = "表示時間:"
		};
		dictionary["BtnResetLayerIndicator"] = new Dictionary<LanguageCode, string>
		{
			[LanguageCode.ZhCn] = "🔄 恢复默认提示徽标样式",
			[LanguageCode.ZhTw] = "🔄 恢復預設提示微標樣式",
			[LanguageCode.En] = "🔄 Reset Indicator Style",
			[LanguageCode.Ja] = "🔄 インジケータースタイルを初期化"
		};
		Translations = dictionary;
	}
}
