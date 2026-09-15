using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>一条参数校验失败记录。</summary>
internal sealed class PluginParameterIssue
{
	/// <summary>对应 <see cref="ParameterField.Key"/>。</summary>
	public string Key { get; init; } = "";

	/// <summary>给人看的字段名（已解析过本地化）。</summary>
	public string Label { get; init; } = "";

	/// <summary>给用户看的中文原因。</summary>
	public string Message { get; init; } = "";

	public override string ToString() =>
		string.IsNullOrEmpty(Label) ? Message : $"{Label}：{Message}";
}

/// <summary>
/// 按 <see cref="ParameterField"/> <b>声明的约束</b>校验参数值。
/// <para>
/// 为什么需要它：<see cref="IActionContribution.Validate"/> 是插件<b>自己写</b>的，
/// 它想看什么就看什么、想漏什么就漏什么。声明了 <c>Required = true</c> 却在 <c>Validate</c> 里
/// 忘了判空是完全可能的 —— 此时宿主若不兜住，一个必填项就会以空值被存进配置，
/// 直到用户触发时才发现「怎么没反应」。
/// </para>
/// <para>
/// 本类只认<b>声明</b>，不猜测插件意图；<see cref="IActionContribution.Validate"/> 仍会在其后被调用，
/// 两者是「宿主底线 + 插件自定义」的关系，而不是二选一。
/// </para>
/// </summary>
internal static class PluginParameterValidator
{
	/// <summary>
	/// 校验一组参数值。
	/// </summary>
	/// <param name="fields">插件声明的字段表。</param>
	/// <param name="values">当前值，可为 <c>null</c>（视为全空）。</param>
	/// <returns>失败项列表；全部通过时为空列表，绝不返回 <c>null</c>。</returns>
	internal static List<PluginParameterIssue> Validate(
		IReadOnlyList<ParameterField>? fields,
		IReadOnlyDictionary<string, string>? values)
	{
		var issues = new List<PluginParameterIssue>();
		if (fields == null || fields.Count == 0) return issues;

		foreach (ParameterField field in fields)
		{
			if (field == null || string.IsNullOrWhiteSpace(field.Key)) continue;

			string label = PluginI18n.ResolveLabel(null, field.LabelKey, field.Label);
			if (string.IsNullOrWhiteSpace(label)) label = field.Key;

			string value = "";
			if (values != null && values.TryGetValue(field.Key, out string? stored))
			{
				value = stored ?? "";
			}

			// ------------------------------------------------------------------ 空值
			if (value.Length == 0)
			{
				// 布尔项未填视为 false，不算「必填未填」。
				// 否则一个默认关闭的开关会强迫用户先手动点一下「我不要」才能保存 ——
				// 把「默认关闭」的语义扭曲成了「必须先声明立场」。
				if (field.Required && field.Type != ParameterFieldType.Bool)
				{
					issues.Add(NewIssue(field, label, "必填项不能为空。"));
				}
				continue;
			}

			// ------------------------------------------------------------------ 长度
			// 声明的 MaxLength 只是插件的期望，真正的硬上限由宿主守着，
			// 防止插件把巨串塞进 config.json 造成 LOH 膨胀。
			int effectiveMax = field.MaxLength is > 0
				? Math.Min(field.MaxLength.Value, PluginApi.MaxParameterValueLength)
				: PluginApi.MaxParameterValueLength;

			if (value.Length > effectiveMax)
			{
				issues.Add(NewIssue(field, label,
					$"长度 {value.Length} 超出上限 {effectiveMax} 个字符。"));
				// 长度已经越界，后面的格式检查意义不大，且正则扫超长串本身是浪费
				continue;
			}

			// ------------------------------------------------------------------ 按类型
			switch (field.Type)
			{
				case ParameterFieldType.Number:
				{
					// 一律用不变文化解析：插件写的是 "0.5"。
					// 若交给当前区域设置，在德语等以逗号作小数点的机器上会解析失败，
					// 同一份配置换个系统就「参数不合法」，属于最难查的那类不一致。
					if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
					{
						issues.Add(NewIssue(field, label, $"「{value}」不是有效数字。"));
						break;
					}

					if (field.Min.HasValue && number < field.Min.Value)
					{
						issues.Add(NewIssue(field, label,
							$"不能小于 {FormatBound(field.Min.Value)}（当前 {FormatBound(number)}）。"));
					}
					else if (field.Max.HasValue && number > field.Max.Value)
					{
						issues.Add(NewIssue(field, label,
							$"不能大于 {FormatBound(field.Max.Value)}（当前 {FormatBound(number)}）。"));
					}
					break;
				}

				case ParameterFieldType.Bool:
				{
					if (!bool.TryParse(value, out _))
					{
						issues.Add(NewIssue(field, label, "只接受 true 或 false。"));
					}
					break;
				}

				case ParameterFieldType.Enum:
				{
					// Options 为空的 Enum 在注册时已被 PluginContext 拦下，
					// 这里再判一次是为了防御「注册后插件又改了 Options」的畸形实现。
					if (field.Options is { Count: > 0 })
					{
						bool matched = false;
						foreach (ParameterOption option in field.Options)
						{
							if (option != null && string.Equals(option.Value, value, StringComparison.Ordinal))
							{
								matched = true;
								break;
							}
						}
						if (!matched)
						{
							issues.Add(NewIssue(field, label, $"「{value}」不是可选项之一。"));
						}
					}
					break;
				}
			}

			// ------------------------------------------------------------------ 正则
			if (!string.IsNullOrEmpty(field.ValidationRegex))
			{
				try
				{
					if (!Regex.IsMatch(value, field.ValidationRegex!, RegexOptions.CultureInvariant))
					{
						issues.Add(NewIssue(field, label, "格式不符合该参数的要求。"));
					}
				}
				catch (ArgumentException ex)
				{
					// 插件给了非法正则 —— 那是插件作者的 bug，不是用户的错。
					// 绝不能因此让用户无法保存，否则用户会以为是自己填错了；
					// 但必须留下痕迹，让作者在日志里看到。
					AppLogger.LogError(
						$"[plugin] 参数 \"{field.Key}\" 的 ValidationRegex 非法，已跳过格式校验：{field.ValidationRegex}",
						ex);
				}
			}
		}

		return issues;
	}

	/// <summary>把失败项压成一行中文，用于执行前拦截时的一句提示。</summary>
	internal static string? DescribeFirst(List<PluginParameterIssue>? issues)
	{
		if (issues == null || issues.Count == 0) return null;
		return issues[0].ToString();
	}

	private static PluginParameterIssue NewIssue(ParameterField field, string label, string message) =>
		new() { Key = field.Key, Label = label, Message = message };

	/// <summary>把范围边界显示成「0」而不是「0.0」，避免让人觉得取值必须是小数。</summary>
	private static string FormatBound(double value) =>
		value == Math.Floor(value) && Math.Abs(value) < 1e15
			? ((long)value).ToString(CultureInfo.InvariantCulture)
			: value.ToString("0.####", CultureInfo.InvariantCulture);
}
