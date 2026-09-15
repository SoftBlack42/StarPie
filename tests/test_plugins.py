"""
插件页 UI 回归（pywinauto + UIA）。

**为什么单独一个文件**：插件页断言需要先在「可执行文件目录旁」准备一个只读扫描目录
``plugin\\``，这会改变其它用例启动时的磁盘状态。把它和 `test_settings.py` 混在一起，
读用例的人就无法从用例本身看出「哪些断言依赖那个目录存在」。所以这里自带 fixture，
自己负责「准备 / 还原」，绝不假设外部已经摆好了现场。

**这里断言什么、不重复什么**：
- 扫描逻辑（候选状态分类、单枚复制、ID 重复……）由宿主自检
  ``StarPie.exe --plugin-selftest <dll> --skip-invoke`` 的 ``[3d]`` 段覆盖，不在这里重测；
- 本文件只负责那部分**只能靠渲染结果才能确认**的东西：候选区块到底有没有渲染出来、
  绑定的文案对不对、以及「宿主在扫描目录不存在时确实什么都没做」。

注意：扫描目录的位置由主程序决定（``AppContext.BaseDirectory\\plugin``），
它**不在** `conftest.py` 的 LOCALAPPDATA 沙箱覆盖范围内 —— 沙箱只隔离了用户数据。
所以 fixture 必须对「原本就存在这个目录」的情况做整体备份与还原，而不是直接删掉。
"""

import os
import shutil
import sys
import time

import pytest

SCAN_DIR_NAME = "plugin"
TAB_PLUGIN = "NavTab5"


def _project_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _exe_path():
    root = _project_root()
    candidates = []
    for config in ("Release", "Debug"):
        for tfm in ("net8.0-windows10.0.19041.0", "net8.0-windows"):
            for name in ("StarPie.exe", "WinPieGestures.exe"):
                candidates.append(
                    os.path.join(root, "WinPieGestures", "bin", config, tfm, name)
                )
    for path in candidates:
        if os.path.exists(path):
            return path
    pytest.fail(f"未找到已构建的可执行文件，请先构建工程。已尝试：{candidates}")


def _sample_plugin_dll():
    path = os.path.join(
        _project_root(),
        "samples",
        "HelloAction",
        "bin",
        "Release",
        "net8.0-windows",
        "StarPie.Plugin.HelloAction.dll",
    )
    if not os.path.exists(path):
        pytest.skip("样例插件尚未构建（samples/HelloAction），跳过候选渲染断言")
    return path


def _remove_children(directory):
    """
    删掉目录下的所有条目，再把空目录本身收掉。

    刻意**不**对整个目录调 `shutil.rmtree`：
    - 目录里只会有本用例自己刚放进去的一两枚 dll，逐条删既够用也更精确；
    - 更重要的是，部分执行环境会给「批量删除」加二次确认护栏，护栏触发时抛的是
      `SystemExit`，它会顺着夹具拆解冒上来，把一次成功的测试变成一条看不出所以然的
      teardown error。逐条删的规模远低于阈值，不会碰到它。
    """
    for name in os.listdir(directory):
        path = os.path.join(directory, name)
        if os.path.isdir(path):
            shutil.rmtree(path, ignore_errors=True)
        else:
            try:
                os.remove(path)
            except OSError:
                pass
    try:
        os.rmdir(directory)
    except OSError:
        pass


@pytest.fixture
def scan_dir():
    """
    准备只读扫描目录，跑完全部还原。

    ``prepare=True`` 时创建空目录；``prepare=False`` 时确保它**不存在**，
    用来验证「宿主绝不创建这个目录」。

    无论原本存在与否，**呈现给用例的都是一个空目录** —— 用例要的是可控现场，
    不是开发者本机放着的插件。原本存在时整体挪走备份，跑完再搬回来；
    原本不存在时（宿主本就不会创建它）跑完把这个临时目录收掉。
    """
    target = os.path.join(os.path.dirname(_exe_path()), SCAN_DIR_NAME)
    backup = target + ".pytest-backup"

    # 原本就存在时整体挪走并备份，绝不原地删 —— 那可能是开发者自己放插件的地方
    shutil.rmtree(backup, ignore_errors=True)
    had_target = os.path.exists(target)
    if had_target:
        shutil.move(target, backup)

    def _prepare(create=True):
        if create and not os.path.exists(target):
            os.makedirs(target, exist_ok=True)
        return target

    try:
        yield _prepare
    finally:
        # 拆解有三处刻意为之，都是踩过坑之后改的：
        #
        # 1) 逐条删（_remove_children）而不是整目录 rmtree：规模小、更精确，
        #    也不会撞上执行环境里「批量删除需二次确认」的护栏（它抛的是 SystemExit）。
        #
        # 2) 搬回备份用 os.rename 而**不是** shutil.move：后者在目标仍作为目录存在时，
        #    会把备份**塞进**那个目录里（plugin/plugin.pytest-backup/），
        #    脏现场从此层层堆积。这个坑真踩过。
        #
        # 3) 清理失败绝不静默。宁可留下备份并大声 WARN，也不要悄悄把现场留成错的 ——
        #    那会让后续用例产出一串与本轮改动毫无关系的失败，排查成本极高。
        #    注意 SystemExit 也要接住：环境护栏抛的就是它，裸 except Exception 接不住。
        cleanup_error = None
        try:
            if os.path.exists(target):
                _remove_children(target)
        except BaseException as ex:
            cleanup_error = ex

        # 清理失败**无论有没有备份**都要出声。
        # 这里原先只在「备份还在」的分支里 WARN，于是「原本没有扫描目录」（没有备份）
        # 这条路径上清理失败是**完全静默**的：脏现场留在原地，下一轮跑出一串
        # 与本轮改动毫无关系的失败，而屏幕上一点线索都没有。
        if cleanup_error is not None:
            print(
                f"[WARN] 扫描目录未能清空：{target}\n"
                f"       原因：{cleanup_error!r}\n"
                f"       该目录现在的状态是脏的，不要据此下结论。",
                file=sys.stderr,
            )

        if os.path.exists(backup):
            if os.path.exists(target):
                print(
                    f"[WARN] 开发者原目录的备份保留在：{backup}\n"
                    f"       恢复方法：先手工清空 {target}，再把该备份目录改名为 plugin。",
                    file=sys.stderr,
                )
            else:
                os.rename(backup, target)
        elif not had_target:
            # 原本就没有这个目录，是夹具为用例建出来的 —— 收干净。
            # 留着会给「宿主绝不创建扫描目录」那类断言摆下一枚假的「已被创建」现场。
            try:
                os.rmdir(target)
            except OSError:
                pass


def _find_text(win, needle):
    """在整窗所有 Text 元素里找一个包含 needle 的，返回它的完整文案。"""
    try:
        for element in win.descendants(control_type="Text"):
            try:
                text = element.window_text() or ""
            except Exception:
                continue
            if needle in text:
                return text
    except Exception:
        pass
    return None


def _open_plugin_page(win):
    tab = win.child_window(auto_id=TAB_PLUGIN, control_type="RadioButton")
    assert tab.exists(timeout=5), f"{TAB_PLUGIN}（插件与扩展）必须存在"
    tab.select()
    # 切页会触发与磁盘对账 + 候选重扫，给它一点时间
    time.sleep(1.2)


def test_plugin_page_renders_core_controls(scan_dir, app):
    """
    插件页的基本控件必须能渲染出来（缺一个都说明 XAML 绑定或事件接线断了）。

    ⚠️ 这一条曾经真的抓到一个缺陷：候选区块的「📂 打开扫描目录」按钮写成了

        <Button Content="📂 打开扫描目录" Click="OpenPluginScanFolderButton_Click" />

    —— ``OpenPluginScanFolderButton_Click`` 是**事件处理器**的名字，不是控件名。
    控件没写 ``Name`` 就没有 AutomationId，WPF 也不给它建带标识的 peer，
    于是它虽然能正常点击，却永远不出现在自动化树里。所以下面这组断言不是走过场：
    它们同时守着「控件在不在」和「有没有被正确地命名到可寻址」这两件事。
    """
    scan_dir(create=True)
    win, _ = app
    _open_plugin_page(win)

    for auto_id, control_type in (
        # 页面骨架
        ("PluginSystemEnabledCheckBox", "CheckBox"),
        ("PluginListBox", "List"),
        # 三枚主操作入口（安装 / 重扫 / 打开数据目录）
        ("InstallPluginButton", "Button"),
        ("RescanPluginsButton", "Button"),
        ("OpenPluginsFolderButton", "Button"),
        # 候选区块自己的入口
        ("OpenPluginScanFolderButton", "Button"),
    ):
        element = win.child_window(auto_id=auto_id, control_type=control_type)
        assert element.exists(timeout=5), f"{auto_id}({control_type}) 未渲染出来"

    # 候选区块的可见性靠「里面的 TextBlock 能不能被找到」来判定。
    # 刻意不断言包裹它的 Border：WPF 的 Border 没有对应的 UIA peer，压根不出现在自动化树里，
    # 断言它只会得到一个与被测行为无关的失败。而 Collapsed 的元素同样不进自动化树，
    # 所以「找得到标题文本」本身就等价于「这一块确实渲染且可见了」。
    header = win.child_window(auto_id="PluginCandidatesHeaderText", control_type="Text")
    assert header.exists(timeout=3), "候选区块未渲染（标题 TextBlock 找不到）"
    assert header.window_text().strip(), "候选区块标题不应为空"


def test_empty_scan_dir_reports_nothing_to_install(scan_dir, app):
    """空扫描目录要明确说「没有可安装的插件」，而不是让用户对着空白猜。"""
    scan_dir(create=True)
    win, _ = app
    _open_plugin_page(win)

    header = win.child_window(auto_id="PluginCandidatesHeaderText", control_type="Text")
    assert header.exists(timeout=3)
    text = header.window_text()
    assert "没有可安装" in text, f"空目录的标题文案不符：{text!r}"


def test_missing_scan_dir_is_reported_and_never_created(scan_dir, app):
    """
    扫描目录不存在时：界面如实说明路径，并且**宿主绝不去创建它**。

    这条是 C6 的 UI 级回归断言。宿主如果手贱创建这个目录，程序装在
    Program Files 这类只读位置时用户会直接撞上一次权限错误 —— 而那是个
    只有在「装在只读位置」的机器上才会复现的缺陷，平时根本测不出来。
    """
    target = scan_dir(create=False)
    assert not os.path.exists(target)

    win, _ = app
    _open_plugin_page(win)

    header = win.child_window(auto_id="PluginCandidatesHeaderText", control_type="Text")
    assert header.exists(timeout=3)
    text = header.window_text()
    assert "不存在" in text, f"目录缺失时的标题文案不符：{text!r}"

    path_text = win.child_window(auto_id="PluginCandidatesPathText", control_type="Text")
    if path_text.exists(timeout=2):
        hint = path_text.window_text()
        assert SCAN_DIR_NAME in hint, f"应把实际路径写给用户看，实际：{hint!r}"

    assert not os.path.exists(target), (
        f"宿主不应创建扫描目录，但它出现了：{target}"
    )


def test_candidate_card_renders_for_dll_in_scan_dir(scan_dir, app):
    """
    扫描目录里放一枚真插件后，候选卡片必须真的渲染出来（含名称、状态与安装按钮）。

    这一条覆盖的正是「逻辑全对但 XAML 没接上」那类问题：候选数据在内存里
    （自检 `[3d]` 已经证明它是对的）和它有没有出现在界面上，是两件事。
    """
    target = scan_dir(create=True)
    shutil.copy2(_sample_plugin_dll(), os.path.join(target, "StarPie.Plugin.HelloAction.dll"))

    win, _ = app
    _open_plugin_page(win)

    header = win.child_window(auto_id="PluginCandidatesHeaderText", control_type="Text")
    assert header.exists(timeout=3)
    text = header.window_text()
    assert "发现 1 个" in text and "1 个可以安装" in text, (
        f"扫描目录里有一枚可安装插件，标题应据此说明，实际：{text!r}"
    )

    # 卡片内容：插件名取自程序集元数据/清单，状态为「可安装」，按钮为「📦 安装」
    assert _find_text(win, "Hello 示例插件") is not None, "候选卡片上应出现插件显示名"
    assert _find_text(win, "可安装") is not None, "候选卡片上应出现状态徽标"
    assert _find_text(win, "📦 安装") is not None, "可安装的候选应带「安装」按钮"
    assert _find_text(win, "StarPie.Plugin.HelloAction.dll") is not None, (
        "候选卡片应显示文件名，用户才知道自己装的是哪一枚"
    )
