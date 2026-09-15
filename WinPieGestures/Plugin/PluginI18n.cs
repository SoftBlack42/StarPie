using System;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件词条 key 的统一解析器。
/// <para>
/// 存在的理由：插件在 <c>II18nRegistry.Register</c> 里写的是<b>短键</b>（如 <c>startTimer</c>），
/// 而注册表实现会在登记时把它归一化成 <c>plugin.&lt;pluginId&gt;.&lt;key&gt;</c>。
/// 但解析侧过去只补了 <c>plugin.</c> 前缀，查的是 <c>plugin.startTimer</c> ——
/// 与登记时真正写入的键<b>永远不相等</b>，本地化名称次次落空，
/// 只因为随后退回 <c>ActionDescriptor.DisplayName</c> 才没把问题暴露出来。
/// </para>
/// <para>
/// 现在动作显示名、参数标签、枚举选项标签都走这里。集中一处的意义在于：
/// 同一个「短键 ⇄ 全键」的换算规则，不该在三个地方各推演一遍，
/// 否则修好一个、漏掉两个只是时间问题。
/// </para>
/// </summary>
internal static class PluginI18n
{
	/// <summary>
	/// 解析插件词条。
	/// </summary>
	/// <param name="pluginId">插件 ID，用于拼出规范键；未知时可传 <c>null</c>。</param>
	/// <param name="key">插件给的短键，或它自己写的完整键。</param>
	/// <returns>命中则返回文案；无可用词条返回 <c>null</c>，调用方应退回插件自带的字面文案。</returns>
	internal static string? Resolve(string? pluginId, string? key)
	{
		if (string.IsNullOrWhiteSpace(key)) return null;

		string trimmed = key.Trim();

		// 插件已经自己写了 plugin. 前缀：尊重它，不要再叠一层，
		// 否则查的会变成 plugin.plugin.xxx —— 又一个静默落空。
		if (trimmed.StartsWith(PluginApi.I18nKeyPrefix, StringComparison.Ordinal))
		{
			return Lookup(trimmed);
		}

		// ① 规范形态：与 II18nRegistry 的归一化结果对齐，这条才是正常会命中的路径。
		if (!string.IsNullOrWhiteSpace(pluginId))
		{
			string? canonical = Lookup($"{PluginApi.I18nKeyPrefix}{pluginId!.Trim()}.{trimmed}");
			if (canonical != null) return canonical;
		}

		// ② 容忍形态：宿主内置词条，或插件把 key 写成了不带插件归属的短键。
		return Lookup($"{PluginApi.I18nKeyPrefix}{trimmed}");
	}

	/// <summary>
	/// 解析插件动作的参数标签。命中不了就退回插件给的字面标签。
	/// </summary>
	internal static string ResolveLabel(string? pluginId, string? labelKey, string literalLabel)
	{
		string? resolved = Resolve(pluginId, labelKey);
		if (!string.IsNullOrWhiteSpace(resolved)) return resolved!;
		return string.IsNullOrWhiteSpace(literalLabel) ? "" : literalLabel;
	}

	/// <summary>
	/// 单次查表。<see cref="I18n.GetString"/> 查不到时会把 key 原样返回，
	/// 因此「返回值等于 key」即表示未命中。
	/// </summary>
	private static string? Lookup(string fullKey)
	{
		try
		{
			string translated = I18n.GetString(fullKey);
			return string.Equals(translated, fullKey, StringComparison.Ordinal) ? null : translated;
		}
		catch (Exception ex)
		{
			// 词条查找运行在设置页的绑定路径上。
			// 查不到词条最多是显示一个短键，绝不能因此让整个「手势与动作」页打不开。
			AppLogger.LogError($"[plugin] 解析词条 \"{fullKey}\" 时异常（已降级为字面文案）", ex);
			return null;
		}
	}
}
