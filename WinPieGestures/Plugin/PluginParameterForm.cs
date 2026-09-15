using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 按插件声明的 <see cref="ParameterField"/> 动态渲染参数表单。
/// <para>
/// 插件<b>不提供 XAML</b>，只声明字段。控件全部由宿主用主程序既有的隐式样式创建，
/// 因此深浅色对比度、圆角、字体、内存占用都由宿主统一保证，
/// 主程序改版也不会让插件界面错位 —— 这是「声明式参数」相对「插件自带界面」的核心收益。
/// </para>
/// <para>
/// 写入策略是<b>写穿</b>：任一控件变化即刻落到 <c>ActionItem.ExtensionData</c>，
/// 由调用方决定何时持久化。这样避免了「表单里有一套值、配置里另有一套值」的中间态，
/// 也就不存在「忘了提交」导致用户以为改了其实没改的情况。
/// </para>
/// </summary>
internal sealed class PluginParameterForm
{
	/// <summary>校验失败提示色。没有对应的主题资源，故在此冻结成静态刷子，避免逐实例分配。</summary>
	private static readonly Brush ErrorBrush = FrozenBrush("#DC2626");

	/// <summary>必填星号色。与失败色同源，但视觉上不该等同「出错了」。</summary>
	private static readonly Brush RequiredBrush = FrozenBrush("#D97706");

	private readonly StackPanel _host;
	private readonly Func<ActionItem?> _targetProvider;
	private readonly Action _onChanged;
	private readonly Dictionary<string, FieldRow> _rows = new(StringComparer.OrdinalIgnoreCase);

	private IReadOnlyList<ParameterField> _fields = Array.Empty<ParameterField>();
	private string? _pluginId;
	private bool _suppress;

	/// <param name="host">承载表单的容器。</param>
	/// <param name="targetProvider">取当前正在编辑的动作；返回 <c>null</c> 时表单只读不写。</param>
	/// <param name="onChanged">任一字段变化后的回调（用于自动保存与预览刷新）。</param>
	internal PluginParameterForm(StackPanel host, Func<ActionItem?> targetProvider, Action onChanged)
	{
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
		_onChanged = onChanged ?? (() => { });
	}

	/// <summary>当前表单是否为空（无任何声明字段）。</summary>
	internal bool IsEmpty => _rows.Count == 0;

	/// <summary>
	/// 依据字段声明重建表单，并回填已保存的值。
	/// <para>
	/// 无参数字段时会清空容器。任何单个字段构建失败都只影响该字段，
	/// 不会让整个「手势与动作」页失去响应 —— 参数表单运行在设置页的刷新路径上。
	/// </para>
	/// </summary>
	internal void Build(IReadOnlyList<ParameterField>? fields, string? pluginId)
	{
		_fields = fields ?? Array.Empty<ParameterField>();
		_pluginId = pluginId;
		Reset();

		if (_fields.Count == 0) return;

		ActionItem? target = _targetProvider();
		IReadOnlyDictionary<string, string>? stored = target?.ExtensionData;

		// 构建期间控件赋值会触发 Changed 事件，必须挡住，否则一打开设置页
		// 就会把「回填」当成「用户修改」写一遍配置并触发自动保存。
		_suppress = true;
		try
		{
			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (ParameterField field in _fields)
			{
				if (field == null || string.IsNullOrWhiteSpace(field.Key)) continue;
				if (!declared.Add(field.Key)) continue;

				TryAddRow(field);
			}

			// 两阶段：先建全所有行，再统一回填。
			// 若边建边填，后建的字段在回填时会看到「前一个字段已被写入」的中间态。
			foreach (FieldRow row in _rows.Values)
			{
				row.Write(ResolveInitialValue(row.Field, stored));
			}

			AppendUndeclaredNotice(stored, declared);
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[plugin] 渲染插件参数表单时发生未预期异常", ex);
			AppendPlainText("参数表单渲染失败，详情见日志。", ErrorBrush, 10.5);
		}
		finally
		{
			_suppress = false;
		}
	}

	/// <summary>清空表单。</summary>
	internal void Reset()
	{
		_rows.Clear();
		if (_host.Children.Count > 0) _host.Children.Clear();
	}

	/// <summary>按声明校验当前表单值。</summary>
	internal List<PluginParameterIssue> Validate()
	{
		ActionItem? target = _targetProvider();
		return PluginParameterValidator.Validate(_fields, target?.ExtensionData);
	}

	/// <summary>
	/// 把校验结果画到对应字段上。传入空列表即清除所有错误标记。
	/// </summary>
	internal void ShowIssues(List<PluginParameterIssue>? issues)
	{
		foreach (FieldRow row in _rows.Values) row.ShowError(null);
		if (issues == null) return;

		foreach (PluginParameterIssue issue in issues)
		{
			if (_rows.TryGetValue(issue.Key, out FieldRow? row)) row.ShowError(issue.Message);
		}
	}

	// ------------------------------------------------------------------ 构建

	private void TryAddRow(ParameterField field)
	{
		string label = PluginI18n.ResolveLabel(_pluginId, field.LabelKey, field.Label);
		if (string.IsNullOrWhiteSpace(label)) label = field.Key;

		try
		{
			( FrameworkElement editor, Func<string> read, Action<string?> write ) = CreateEditor(field);

			var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

			// ---- 标签行
			var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

			var labelText = new TextBlock
			{
				Text = label,
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
			labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
			labelRow.Children.Add(labelText);

			if (field.Required && field.Type != ParameterFieldType.Bool)
			{
				labelRow.Children.Add(new TextBlock
				{
					Text = " *",
					FontSize = 11,
					Foreground = RequiredBrush,
					VerticalAlignment = VerticalAlignment.Center,
					ToolTip = "必填项",
				});
			}

			container.Children.Add(labelRow);

			// ---- 编辑器
			container.Children.Add(editor);

			// ---- 说明行（帮助文本优先；没有则用占位提示顶替，省掉自绘 TextBox 的水印层）
			string help = field.HelpText ?? "";
			if (string.IsNullOrWhiteSpace(help) && !string.IsNullOrWhiteSpace(field.Placeholder))
			{
				help = "例：" + field.Placeholder;
			}
			if (field.Type == ParameterFieldType.Number && (field.Min.HasValue || field.Max.HasValue))
			{
				string range = DescribeRange(field);
				help = string.IsNullOrWhiteSpace(help) ? range : $"{help}（{range}）";
			}
			if (!string.IsNullOrWhiteSpace(help))
			{
				var helpText = new TextBlock
				{
					Text = help,
					FontSize = 10.5,
					Margin = new Thickness(0, 3, 0, 0),
					TextWrapping = TextWrapping.Wrap,
				};
				helpText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
				container.Children.Add(helpText);
			}

			if (!string.IsNullOrWhiteSpace(field.Placeholder)) editor.ToolTip = field.Placeholder;

			// ---- 错误行
			var errorText = new TextBlock
			{
				FontSize = 10.5,
				Margin = new Thickness(0, 3, 0, 0),
				Foreground = ErrorBrush,
				TextWrapping = TextWrapping.Wrap,
				Visibility = Visibility.Collapsed,
			};
			container.Children.Add(errorText);

			_host.Children.Add(container);

			_rows[field.Key] = new FieldRow
			{
				Field = field,
				Read = read,
				Write = write,
				ShowError = message =>
				{
					if (string.IsNullOrWhiteSpace(message))
					{
						errorText.Text = "";
						errorText.Visibility = Visibility.Collapsed;
					}
					else
					{
						errorText.Text = "⚠️ " + message;
						errorText.Visibility = Visibility.Visible;
					}
				},
			};

			// 兜底：创建成功后立刻读一次，把插件声明的默认值落盘。
			_ = read;
		}
		catch (Exception ex)
		{
			// 单个字段坏掉不该毁掉整张表单。这通常意味着插件给的声明畸形（比如 Number 却配了非法 Min）。
			AppLogger.LogError($"[plugin] 渲染参数字段 \"{field.Key}\" 失败", ex);
			AppendPlainText($"参数「{label}」无法渲染：{ex.GetBaseException().Message}", ErrorBrush, 10.5);
		}
	}

	/// <summary>
	/// 按类型创建编辑器。返回「读当前值」与「把值写进控件」两个闭包，
	/// 让其余流程与控件具体类型无关。
	/// </summary>
	private ( FrameworkElement Editor, Func<string> Read, Action<string?> Write ) CreateEditor(ParameterField field)
	{
		switch (field.Type)
		{
			case ParameterFieldType.MultilineText:
			{
				var box = Styled(new TextBox
				{
					MinHeight = 56,
					MaxHeight = 110,
					AcceptsReturn = true,
					TextWrapping = TextWrapping.Wrap,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
					FontSize = 11.5,
					VerticalContentAlignment = VerticalAlignment.Top,
					Padding = new Thickness(6, 4, 6, 4),
				});
				box.TextChanged += (_, _) => Commit(field.Key, box.Text);
				return (box, () => box.Text ?? "", value => box.Text = value ?? "");
			}

			case ParameterFieldType.Number:
			{
				var box = Styled(new TextBox { Height = 30, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center });
				box.TextChanged += (_, _) => Commit(field.Key, NormalizeNumber(box.Text));
				return (box, () => box.Text ?? "", value => box.Text = value ?? "");
			}

			case ParameterFieldType.Bool:
			{
				var check = Styled(new CheckBox { VerticalAlignment = VerticalAlignment.Center });
				check.Checked += (_, _) => Commit(field.Key, "true");
				check.Unchecked += (_, _) => Commit(field.Key, "false");
				return (
					check,
					// 布尔值始终显式落盘为 true/false，不像其他类型那样「空即删除」。
					// 否则用户取消勾选后配置里什么都不剩，插件读到的将是它自己的兜底值 ——
					// 而那个兜底值可能是 true，「取消勾选」于是表现为「没生效」。
					() => check.IsChecked == true ? "true" : "false",
					value => check.IsChecked = bool.TryParse(value, out bool b) && b);
			}

			case ParameterFieldType.Enum:
			{
				var combo = Styled(new ComboBox
				{
					Height = 30,
					FontSize = 11.5,
					DisplayMemberPath = "Label",
					SelectedValuePath = "Value",
				});

				foreach (ParameterOption option in field.Options ?? Array.Empty<ParameterOption>())
				{
					if (option == null) continue;
					string optionLabel = PluginI18n.ResolveLabel(_pluginId, option.LabelKey, option.Label);
					if (string.IsNullOrWhiteSpace(optionLabel)) optionLabel = option.Value;
					combo.Items.Add(new EnumItem { Value = option.Value, Label = optionLabel });
				}

				combo.SelectionChanged += (_, _) =>
				{
					if (combo.SelectedItem is EnumItem item) Commit(field.Key, item.Value);
				};

				return (
					combo,
					() => combo.SelectedItem is EnumItem item ? item.Value : "",
					value =>
					{
						combo.SelectedItem = null;
						if (string.IsNullOrEmpty(value)) return;
						foreach (object candidate in combo.Items)
						{
							if (candidate is EnumItem item &&
								string.Equals(item.Value, value, StringComparison.Ordinal))
							{
								combo.SelectedItem = item;
								return;
							}
						}
						// 声明里没有这个值（插件升级后改了选项表）：保持未选中，
						// 让用户看到「这个参数当前没有合法取值」，而不是悄悄显示成第一个选项。
					});
			}

			case ParameterFieldType.Folder:
			case ParameterFieldType.File:
			{
				bool isFolder = field.Type == ParameterFieldType.Folder;
				var grid = new Grid();
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

				var box = Styled(new TextBox { Height = 30, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center });
				box.TextChanged += (_, _) => Commit(field.Key, box.Text);
				Grid.SetColumn(box, 0);
				grid.Children.Add(box);

				var browse = new Button
				{
					Content = isFolder ? "📂 浏览…" : "📄 选择…",
					Height = 30,
					Padding = new Thickness(8, 0, 8, 0),
					Margin = new Thickness(6, 0, 0, 0),
					FontSize = 11.5,
				};
				ApplyButtonStyle(browse);
				browse.Click += (_, _) =>
				{
					string picked = isFolder ? BrowseFolder(box.Text) : BrowseFile(box.Text);
					if (string.IsNullOrEmpty(picked)) return;
					box.Text = picked;
				};
				Grid.SetColumn(browse, 1);
				grid.Children.Add(browse);

				return (grid, () => box.Text ?? "", value => box.Text = value ?? "");
			}

			case ParameterFieldType.Hotkey:
			{
				var recorder = Styled(new HotkeyRecorderBox { Height = 30, FontSize = 11.5 });
				recorder.HotkeyChanged += (_, hotkey) => Commit(field.Key, hotkey ?? "");
				return (
					recorder,
					() => recorder.HotkeyText ?? "",
					value => recorder.HotkeyText = value ?? "");
			}

			case ParameterFieldType.Color:
			{
				var grid = new Grid();
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

				var swatch = new Border
				{
					Width = 22,
					Height = 22,
					CornerRadius = new CornerRadius(4),
					Margin = new Thickness(0, 0, 8, 0),
					VerticalAlignment = VerticalAlignment.Center,
					BorderThickness = new Thickness(1),
					BorderBrush = FrozenBrush("#94A3B8"),
					Background = FrozenBrush("#FF2563EB"),
				};
				Grid.SetColumn(swatch, 0);
				grid.Children.Add(swatch);

				var box = Styled(new TextBox { Height = 30, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center });
				box.TextChanged += (_, _) =>
				{
					UpdateSwatch(swatch, box.Text);
					Commit(field.Key, box.Text);
				};
				Grid.SetColumn(box, 1);
				grid.Children.Add(box);

				var pick = new Button
				{
					Content = "🎨 选色",
					Height = 30,
					Padding = new Thickness(8, 0, 8, 0),
					Margin = new Thickness(6, 0, 0, 0),
					FontSize = 11.5,
				};
				ApplyButtonStyle(pick);
				pick.Click += (_, _) =>
				{
					string? picked = PickColor(box.Text);
					if (picked == null) return;
					box.Text = picked;
					UpdateSwatch(swatch, picked);
				};
				Grid.SetColumn(pick, 2);
				grid.Children.Add(pick);

				return (
					grid,
					() => box.Text ?? "",
					value =>
					{
						box.Text = value ?? "";
						UpdateSwatch(swatch, box.Text);
					});
			}

			default:
			{
				var box = Styled(new TextBox { Height = 30, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center });
				if (field.MaxLength is > 0) box.MaxLength = Math.Min(field.MaxLength.Value, PluginApi.MaxParameterValueLength);
				box.TextChanged += (_, _) => Commit(field.Key, box.Text);
				return (box, () => box.Text ?? "", value => box.Text = value ?? "");
			}
		}
	}

	// ------------------------------------------------------------------ 值处理

	/// <summary>
	/// 决定一个字段初次显示什么：已保存值 → 声明默认值 → 空。
	/// </summary>
	private static string ResolveInitialValue(ParameterField field, IReadOnlyDictionary<string, string>? stored)
	{
		if (stored != null && stored.TryGetValue(field.Key, out string? saved) && saved != null)
		{
			return saved;
		}

		string fallback = field.DefaultValue ?? "";
		if (field.Type == ParameterFieldType.Bool)
		{
			// 布尔项的默认值可能是 "True"/"1"/"yes"，统一归一化成 true/false，
			// 否则勾选状态会因写法差异而判断错。
			return IsTruthy(fallback) ? "true" : "false";
		}

		// 枚举的默认值若不在选项表里，就当没给 —— 否则会填入一个用户无法选中的值。
		if (field.Type == ParameterFieldType.Enum && fallback.Length > 0 && field.Options is { Count: > 0 })
		{
			foreach (ParameterOption option in field.Options)
			{
				if (option != null && string.Equals(option.Value, fallback, StringComparison.Ordinal))
				{
					return fallback;
				}
			}
			return "";
		}

		return fallback;
	}

	private static bool IsTruthy(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		string trimmed = value.Trim();
		if (bool.TryParse(trimmed, out bool parsed)) return parsed;
		return trimmed == "1" || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 把数字文本归一化成不变文化形式再落盘。
	/// 只在文本确实能解析成数字时归一化，因此不会打断「用户正打到一半」的输入。
	/// </summary>
	private static string NormalizeNumber(string? raw)
	{
		string text = raw ?? "";
		if (text.Length == 0) return "";
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
			? number.ToString("0.####", CultureInfo.InvariantCulture)
			: text;
	}

	/// <summary>
	/// 写穿到 <c>ActionItem.ExtensionData</c>。
	/// 空值一律<b>删除键</b>而非写入空串：这样「未填写」在配置里有唯一表示，
	/// 也不会让 config.json 被一堆空 KV 撑大。
	/// </summary>
	private void Commit(string key, string? value)
	{
		if (_suppress) return;
		if (string.IsNullOrEmpty(key)) return;

		ActionItem? target = _targetProvider();
		if (target == null) return;

		string text = value ?? "";

		try
		{
			if (text.Length == 0)
			{
				if (target.ExtensionData == null) return;
				if (!target.ExtensionData.Remove(key)) return;
				if (target.ExtensionData.Count == 0) target.ExtensionData = null;
			}
			else
			{
				// 单个值超长直接截断而不是拒绝：上限是宿主的内存红线，
				// 不该变成一个用户看不懂的保存失败。
				if (text.Length > PluginApi.MaxParameterValueLength)
				{
					text = text.Substring(0, PluginApi.MaxParameterValueLength);
				}

				target.ExtensionData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				if (target.ExtensionData.TryGetValue(key, out string? existing) &&
					string.Equals(existing, text, StringComparison.Ordinal))
				{
					return; // 值没变，不必触发自动保存
				}
				target.ExtensionData[key] = text;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"[plugin] 写入参数 \"{key}\" 失败", ex);
			return;
		}

		_onChanged();
	}

	/// <summary>
	/// 提示「配置里有当前插件版本未声明的参数」。
	/// 不删除它们（向前兼容：插件升级后可能重新用到），但也不能让它们彻底隐形 ——
	/// 否则用户会以为配置丢了，而插件作者会以为自己写错了键名。
	/// </summary>
	private void AppendUndeclaredNotice(IReadOnlyDictionary<string, string>? stored, HashSet<string> declared)
	{
		if (stored == null || stored.Count == 0) return;

		var unknown = new List<string>();
		foreach (string key in stored.Keys)
		{
			if (!declared.Contains(key)) unknown.Add(key);
		}
		if (unknown.Count == 0) return;

		unknown.Sort(StringComparer.OrdinalIgnoreCase);
		AppendPlainText(
			$"另有 {unknown.Count} 项已保存的取值未在当前版本声明：{string.Join("、", unknown)}。" +
			"可能是插件升级后改名或移除了参数，这些值会被保留但不会生效。",
			null,
			10.5);
	}

	// ------------------------------------------------------------------ 编辑辅助

	private static string DescribeRange(ParameterField field)
	{
		if (field.Min.HasValue && field.Max.HasValue) return $"取值 {Format(field.Min.Value)} ~ {Format(field.Max.Value)}";
		if (field.Min.HasValue) return $"不小于 {Format(field.Min.Value)}";
		if (field.Max.HasValue) return $"不大于 {Format(field.Max.Value)}";
		return "";

		static string Format(double value) =>
			value == Math.Floor(value) && Math.Abs(value) < 1e15
				? ((long)value).ToString(CultureInfo.InvariantCulture)
				: value.ToString("0.####", CultureInfo.InvariantCulture);
	}

	private static void UpdateSwatch(Border swatch, string? hex)
	{
		if (swatch == null) return;
		try
		{
			if (!string.IsNullOrWhiteSpace(hex) &&
				ColorConverter.ConvertFromString(hex.Trim()) is Color color)
			{
				swatch.Background = new SolidColorBrush(color);
				return;
			}
		}
		catch
		{
			// 用户打到一半的非法色值：保持上一个颜色即可，不必报错打扰
		}
		swatch.Background = FrozenBrush("#FF94A3B8");
	}

	private string BrowseFolder(string? current)
	{
		try
		{
			using var dialog = new System.Windows.Forms.FolderBrowserDialog();
			if (!string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current))
			{
				dialog.SelectedPath = current!;
			}
			return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : "";
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[plugin] 打开文件夹选择器失败", ex);
			return "";
		}
	}

	private string BrowseFile(string? current)
	{
		try
		{
			var dialog = new Microsoft.Win32.OpenFileDialog { CheckFileExists = true };
			if (!string.IsNullOrWhiteSpace(current))
			{
				try
				{
					string? dir = System.IO.Path.GetDirectoryName(current);
					if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) dialog.InitialDirectory = dir;
					dialog.FileName = System.IO.Path.GetFileName(current);
				}
				catch
				{
					// 路径畸形就退回默认目录，不该因此打不开选择器
				}
			}
			return dialog.ShowDialog(Window.GetWindow(_host)) == true ? dialog.FileName : "";
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[plugin] 打开文件选择器失败", ex);
			return "";
		}
	}

	private string? PickColor(string? current)
	{
		try
		{
			string seed = string.IsNullOrWhiteSpace(current) ? "#FF2563EB" : current!.Trim();
			var picker = new ColorPickerWindow(seed);
			Window? owner = Window.GetWindow(_host);
			if (owner != null && !ReferenceEquals(owner, picker)) picker.Owner = owner;

			return picker.ShowDialog() == true ? picker.SelectedHexColor : null;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[plugin] 打开取色器失败", ex);
			return null;
		}
	}

	/// <summary>
	/// 给代码创建的控件套上本窗口的隐式样式。
	/// <para>
	/// WPF 本来会在元素挂进视觉树时自动套用隐式样式，但这里是先构造、
	/// 再挂到 <see cref="StackPanel"/>、最后才进入窗口树，时机上多一层不确定性。
	/// 显式取一次资源可以彻底避免「插件参数框样式和主界面不一致」这类只偶尔出现的问题。
	/// </para>
	/// </summary>
	private T Styled<T>(T element) where T : FrameworkElement
	{
		try
		{
			if (element.Style == null && _host.TryFindResource(element.GetType()) is Style style)
			{
				element.Style = style;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"[plugin] 套用 {element.GetType().Name} 样式失败（已用默认外观）", ex);
		}
		return element;
	}

	/// <summary>按钮的隐式样式键是字符串而非类型，故单独处理。</summary>
	private void ApplyButtonStyle(Button button)
	{
		try
		{
			if (button.Style == null && _host.TryFindResource("ModernButtonStyle") is Style style)
			{
				button.Style = style;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("[plugin] 套用按钮样式失败（已用默认外观）", ex);
		}
	}

	private void AppendPlainText(string text, Brush? foreground, double fontSize)
	{
		var block = new TextBlock
		{
			Text = text,
			FontSize = fontSize,
			Margin = new Thickness(0, 0, 0, 8),
			TextWrapping = TextWrapping.Wrap,
		};
		if (foreground != null) block.Foreground = foreground;
		else block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

		_host.Children.Add(block);
	}

	private static Brush FrozenBrush(string hex)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		// 冻结后跨线程共享是安全的，也省掉每次使用时的变更通知开销（项目内存红线的一部分）
		brush.Freeze();
		return brush;
	}

	/// <summary>一个字段的编辑契约。把「控件类型」全部收束到两个闭包里。</summary>
	private sealed class FieldRow
	{
		public ParameterField Field = new();
		public Func<string> Read = () => "";
		public Action<string?> Write = _ => { };
		public Action<string?> ShowError = _ => { };
	}

	/// <summary>枚举下拉项：显示本地化标签，但落盘的始终是插件约定的 Value。</summary>
	private sealed class EnumItem
	{
		public string Value { get; init; } = "";
		public string Label { get; init; } = "";
	}
}
