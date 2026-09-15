namespace StarPie.Plugin;

/// <summary>动作贡献点注册表。这是 P0 阶段唯一「必用」的注册入口。</summary>
public interface IActionRegistry
{
    /// <summary>
    /// 注册一个动作贡献点。
    /// <para>
    /// 返回的 <see cref="IDisposable"/> 是撤销凭据：<c>Dispose()</c> 即摘除该贡献点。
    /// 宿主在停用插件时会<b>兜底撤销全部凭据</b>，即使插件忘记释放也不会残留（双保险）。
    /// </para>
    /// </summary>
    /// <exception cref="PluginContractException">
    /// 描述符非法（ID 为空 / 含非法字符）、ID 冲突、或注册发生在 <c>Initialize</c> 之外。
    /// </exception>
    IDisposable Register(IActionContribution contribution);
}

/// <summary>多语言词条注册表。</summary>
public interface II18nRegistry
{
    /// <summary>
    /// 注册词条。传入的是<b>短键</b>（如 <c>timer.start</c>），宿主自动加上 <c>plugin.&lt;pluginId&gt;.</c> 前缀。
    /// </summary>
    /// <param name="key">短键，建议 <c>模块名.字段名</c> 全小写点分风格。</param>
    /// <param name="zhCn">简体中文文案（必填，作为兜底语言）。</param>
    /// <param name="en">英文文案；传 null 时英文环境回退到中文。</param>
    void Register(string key, string zhCn, string? en = null);

    /// <summary>批量注册某一语言的完整词条表（短键 → 文案）。</summary>
    void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table);

    /// <summary>
    /// 按短键取当前语言文案（宿主自动补前缀）。
    /// 找不到时返回 <paramref name="fallback"/>，再找不到则返回短键本身，绝不抛异常。
    /// </summary>
    string T(string key, string? fallback = null);
}

/// <summary>矢量图标注册表。</summary>
public interface IIconRegistry
{
    /// <summary>
    /// 注册一枚 SVG 矢量图标。
    /// </summary>
    /// <param name="key">短键（如 <c>timer</c>），宿主自动归一化为 <c>plugin:&lt;pluginId&gt;:&lt;key&gt;</c>。</param>
    /// <param name="svgPathData">SVG 的 <c>path</c> 数据（<c>d</c> 属性内容），例如 <c>"M4,4 L20,20 …"</c>。单位按 24×24 视口设计。</param>
    /// <returns>可直接填入 <see cref="ActionDescriptor.IconKey"/> 的完整 key。</returns>
    string RegisterSvg(string key, string svgPathData);

    /// <summary>按短键取回完整图标 key；未注册时返回 null。</summary>
    string? ResolveKey(string key);
}
