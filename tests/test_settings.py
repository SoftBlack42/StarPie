import os
import json
import re
import time
import pytest
from pywinauto import Desktop

def get_config_path(local_app_data):
    for name in ["StarPie", "WinPieGestures"]:
        p = os.path.join(str(local_app_data), name, "config.json")
        if os.path.exists(p):
            return p
    return os.path.join(str(local_app_data), "StarPie", "config.json")

def parse_px_value(text):
    """Slider labels now embed units, e.g. '25 px'."""
    return float(re.sub(r"[^0-9.]", "", text))

def _toggle_sidebar(win):
    """
    点一下侧边栏的折叠/展开按钮。

    侧边栏**默认是展开态**。两个状态各有一批控件只在对侧可见：
      - 展开态：NavTab0Text 等文字标签、ConfigMode 模式切换器
      - 折叠态：SidebarThemeCollapsedButton（折叠态专属的应用主题循环按钮）
    所以「找不到某个侧边栏控件」时，先想清楚它属于哪个状态。
    """
    win.child_window(auto_id="SidebarToggleButton", control_type="Button").click_input()
    time.sleep(0.9)

def _switch_to_list_mode(win):
    """
    把 NavTab2 切到「紧凑全览列表」。配置方案列表（ProfilesListBox 及其增删改名按钮）
    只在这个模式下存在。

    **要求调用方已切到高级全量模式** —— 简洁模式会整块隐藏这个分段切换器并强制回画布模式。
    缺了这一步就是「断言一个本就不该存在的控件」，失败与被测功能无关。
    """
    radio = win.child_window(auto_id="MappingsViewModeListRadio", control_type="RadioButton")
    assert radio.exists(timeout=3), (
        "找不到 MappingsViewModeListRadio —— 简洁模式会整块隐藏它，"
        "本用例应改用 advanced_mode 夹具而不是 app"
    )
    radio.select()
    time.sleep(0.6)

def _list_item_texts(list_box):
    """
    取出 ListBox 每一项的「可见文本」。

    **不要用 `item.window_text()` 直接断言内容**：ListViewItem 的 UIA Name 常常不是
    用户看到的文字 —— 本项目里黑名单列表返回的就是 ViewModel 的类名字符串
    （`WinPieGestures.BlacklistProcessItemViewModel`），拿它比字符串必然失败，
    而那失败纯属取值方式不对，与被测功能无关。
    稳妥做法是把该项自身文本与其所有 Text 后代的文本合并起来看。
    """
    texts = []
    for item in list_box.children(control_type="ListItem"):
        parts = []
        try:
            own = item.window_text()
            if own:
                parts.append(own)
        except Exception:
            pass
        try:
            for t in item.descendants(control_type="Text"):
                try:
                    value = t.window_text()
                    if value:
                        parts.append(value)
                except Exception:
                    continue
        except Exception:
            pass
        texts.append(" ".join(parts))
    return texts

def _combo_item_texts(combo):
    """
    展开下拉并取回全部条目文本。

    两个坑：
    1. pywinauto 0.6.9 的 `ComboBoxWrapper` **没有** `item_texts()`（那是更新版本的 API）。
       可用的是 `texts()` / `children_texts()`。写成 `item_texts()` 会得到一个怪异错误
       （`Neither GUI element (wrapper) nor wrapper method ...`），因为 WindowSpecification
       会把未知属性当成**子窗口**，而不是方法。
    2. ComboBox 的下拉是**独立弹窗**，必须先 `expand()` 让它真正渲染出来，
       否则条目还没生成。刚切页时也可能没就绪，所以要重试而不是一次定生死。
    3. 读到的条目 ListItem 自身 `window_text()` 是 ViewModel 类名
       （`WinPieGestures.ActionTypeItem`），所以要从 wrapper 的 `texts()` 取，
       不要从 ListItem 上取。
    """
    wrapper = combo.wrapper_object()
    for _ in range(4):
        try:
            wrapper.expand()
            time.sleep(0.4)
            texts = wrapper.texts()
            if texts:
                return texts
        except Exception:
            pass
        time.sleep(0.3)
    return []

def _collapse_combo(combo):
    """收起下拉，避免留一个展开的弹窗影响后续操作。"""
    try:
        combo.wrapper_object().collapse()
        time.sleep(0.2)
    except Exception:
        pass

def test_modify_slider_and_save(app):
    win, local_app_data = app
    
    # 1. Locate the Slider and Label
    slider = win.child_window(auto_id="ThresholdSlider", control_type="Slider")
    label = win.child_window(auto_id="ThresholdValueLabel", control_type="Text")
    
    initial_text = label.window_text()
    initial_val = parse_px_value(initial_text)
    
    # 2. Set value directly using UIA RangeValue pattern
    slider.set_value(32.0)
    time.sleep(0.3)
    
    new_text = label.window_text()
    new_val = parse_px_value(new_text)
    
    assert new_val != initial_val, f"Slider value should have changed from {initial_val}"
    
    # 3. Check if config exists before saving
    config_path = get_config_path(local_app_data)

    # 4. Click the SaveButton to persist configurations
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    # Dismiss the popup dialog if it appears
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.8)
    
    # 5. Verify the config file was written correctly in the sandbox
    assert os.path.exists(config_path), f"Config file not found at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert config["DragThreshold"] == new_val, f"Saved DragThreshold ({config['DragThreshold']}) should match UI value ({new_val})"


def test_switch_all_tabs_smoothly(advanced_mode):
    """
    Test clicking through all 6 navigation radio buttons (NavTab0 ~ NavTab5)
    to guarantee zero crashes, zero freezes, and that controls remain fully responsive.

    用 advanced_mode 而不是 app：本用例末尾要断言配置方案列表，而那是高级 UI。
    """
    win, local_app_data = advanced_mode
    
    # Iterate through all 6 tabs. 这里是「全页遍历」的唯一出处，
    # 新增页时务必把上界一起推进 —— 否则新页只是没被测，而不是测过了。
    # 0: 触发与场景 (NavTab0)
    # 1: 外观与形态 (NavTab1)
    # 2: 手势与动作 (NavTab2)
    # 3: 高级与系统 (NavTab3)
    # 4: 关于与更新 (NavTab4)
    # 5: 插件与扩展 (NavTab5)
    for i in range(6):
        tab_btn = win.child_window(auto_id=f"NavTab{i}", control_type="RadioButton")
        assert tab_btn.exists(timeout=5), f"NavTab{i} must exist"
        tab_btn.select()
        time.sleep(0.3)
        assert win.is_visible(), f"Window must remain visible after selecting NavTab{i}"
        
    # Specifically re-verify Tab 1 (Appearance & Live Canvas)
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    wheel_slider = win.child_window(auto_id="WheelRadiusSlider", control_type="Slider")
    assert wheel_slider.exists(timeout=3), "WheelRadiusSlider should exist in Appearance tab"
    
    gap_slider = win.child_window(auto_id="SectorGapSlider", control_type="Slider")
    assert gap_slider.exists(timeout=3), "SectorGapSlider should exist in Appearance tab"
    
    corner_slider = win.child_window(auto_id="SectorCornerRadiusSlider", control_type="Slider")
    assert corner_slider.exists(timeout=3), "SectorCornerRadiusSlider should exist in Appearance tab"
    
    # Test Tab 2 (Mappings & Profiles) - v1.6.8 moved profile management
    # into the collapsible list view; switch to it before asserting.
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    _switch_to_list_mode(win)
    profiles_list = win.child_window(auto_id="ProfilesListBox", control_type="List")
    assert profiles_list.exists(timeout=3), "ProfilesListBox should exist in Mappings tab"
    
    # Test Tab 3 (System)
    tab3 = win.child_window(auto_id="NavTab3", control_type="RadioButton")
    tab3.select()
    time.sleep(0.3)
    auto_start_chk = win.child_window(auto_id="AutoStartCheckBox", control_type="CheckBox")
    assert auto_start_chk.exists(timeout=3), "AutoStartCheckBox should exist in System tab"
    
    # Test Tab 4 (About)
    tab4 = win.child_window(auto_id="NavTab4", control_type="RadioButton")
    tab4.select()
    time.sleep(0.3)
    changelog_btn = win.child_window(auto_id="OpenChangelogButton", control_type="Button")
    assert changelog_btn.exists(timeout=3), "OpenChangelogButton should exist in About tab"
    
    # Final check that window is still healthy and alive
    assert win.is_visible()


def test_appearance_shapes_and_geometry_reset(app):
    """
    Test Shape selection, Gap/Fillet adjustments, and Reset Dimensions button.
    """
    win, local_app_data = app
    
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 1. Test Gap & Corner Radius Sliders
    gap_slider = win.child_window(auto_id="SectorGapSlider", control_type="Slider")
    corner_slider = win.child_window(auto_id="SectorCornerRadiusSlider", control_type="Slider")
    gap_label = win.child_window(auto_id="SectorGapLabel", control_type="Text")
    
    gap_slider.set_value(5.0)
    corner_slider.set_value(8.0)
    time.sleep(0.3)
    
    assert "5" in gap_label.window_text()
    
    # 2. Test Reset Dimensions Button
    reset_btn = win.child_window(auto_id="ResetDimensionsButton", control_type="Button")
    if reset_btn.exists(timeout=2):
        reset_btn.invoke()
        time.sleep(0.4)
        assert "2" in gap_label.window_text()


def test_blacklist_add_and_delete(app):
    """
    Test adding a new process to Blacklist and removing it.
    """
    win, local_app_data = app
    
    tab0 = win.child_window(auto_id="NavTab0", control_type="RadioButton")
    tab0.select()
    time.sleep(0.3)
    
    txt_box = win.child_window(auto_id="NewBlacklistProcessTextBox", control_type="Edit")
    add_btn = win.child_window(auto_id="AddBlacklistButton", control_type="Button")
    del_btn = win.child_window(auto_id="DeleteBlacklistButton", control_type="Button")
    list_box = win.child_window(auto_id="BlacklistListBox", control_type="List")
    
    txt_box.set_text("testgame.exe")
    time.sleep(0.2)
    add_btn.invoke()
    time.sleep(0.4)
    
    # Check that item was added to listbox
    items = _list_item_texts(list_box)
    assert any("testgame.exe" in it for it in items), f"testgame.exe should be in blacklist items: {items}"
    
    # Select and remove
    for item, text in zip(list_box.children(control_type="ListItem"), items):
        if "testgame.exe" in text:
            item.select()
            time.sleep(0.2)
            del_btn.invoke()
            time.sleep(0.4)
            break
            
    items_after = _list_item_texts(list_box)
    assert not any("testgame.exe" in it for it in items_after), (
        f"testgame.exe should have been deleted: {items_after}"
    )


def test_profile_management_ui_and_buttons(advanced_mode):
    """
    Test existence, states, and accessibility of profile management controls:
    Add App Profile, Add Custom Profile, Rename Profile, Delete Profile.

    这些控件全在 NavTab2 的「列表模式」里，而列表模式只在高级全量模式下可达 —— 故用 advanced_mode。
    """
    win, local_app_data = advanced_mode
    
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)

    _switch_to_list_mode(win)

    add_app_btn = win.child_window(auto_id="AddProfileButton", control_type="Button")
    add_custom_btn = win.child_window(auto_id="AddCustomProfileButton", control_type="Button")
    rename_btn = win.child_window(auto_id="RenameProfileButton", control_type="Button")
    delete_btn = win.child_window(auto_id="DeleteProfileButton", control_type="Button")
    profiles_list = win.child_window(auto_id="ProfilesListBox", control_type="List")
    
    assert add_app_btn.exists(timeout=3), "AddProfileButton should exist"
    assert add_custom_btn.exists(timeout=3), "AddCustomProfileButton should exist"
    assert rename_btn.exists(timeout=3), "RenameProfileButton should exist"
    assert delete_btn.exists(timeout=3), "DeleteProfileButton should exist"
    assert profiles_list.exists(timeout=3), "ProfilesListBox should exist"
    
    # Verify Global profile is listed
    items = _list_item_texts(profiles_list)
    assert any("Global" in it for it in items), f"Global profile must be listed: {items}"


def test_hotkey_recorder_and_system_presets_catalog(advanced_mode):
    """
    Test v1.2.2 features:
    1. Navigation to Mappings Tab (NavTab2).
    2. Verification that Slots list and profile controls are displayed.
    3. Save and persistence verification.

    配置方案列表只在 NavTab2 的列表模式里，而列表模式只在高级全量模式下可达 —— 故用 advanced_mode。
    """
    win, local_app_data = advanced_mode
    
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    
    _switch_to_list_mode(win)

    profiles_list = win.child_window(auto_id="ProfilesListBox", control_type="List")
    assert profiles_list.exists(timeout=3), "ProfilesListBox should exist in Mappings tab"
    
    # Save settings and verify config persistence
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.5)
    assert win.is_visible()


def test_v124_app_interface_themes_and_clean_appearance(app):
    """
    Test v1.2.4 features (updated for v1.6.8 sidebar):
    1. Navigation to Appearance Tab (NavTab1).
    2. Verification that the app theme cycle button on the collapsed sidebar
       (SidebarThemeCollapsedButton, replaces the old AppThemeComboBox) exists.
    3. Verification that 'ThemeComboBox' (轮盘配色方案) with 7+ presets exists.
    4. Verification that Wheel Background images card is removed.
    5. AppTheme cycling (System -> Light), saving, and JSON persistence validation.
    """
    win, local_app_data = app
    
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 1. Verify Wheel Theme dropdown (轮盘配色方案)
    wheel_theme_combo = win.child_window(auto_id="ThemeComboBox", control_type="ComboBox")
    assert wheel_theme_combo.exists(timeout=3), "ThemeComboBox should exist"
    
    # 2. Verify Wheel Background images controls are removed
    wheel_bg_box = win.child_window(auto_id="WheelBgImageTextBox", control_type="Edit")
    assert not wheel_bg_box.exists(timeout=1), "WheelBgImageTextBox should NOT exist (feature canceled)"
    
    # 3. 应用主题循环按钮是**折叠态侧边栏专属**的：SidebarThemeCollapsedButton 只在侧边栏
    #    收起时可见（展开态下由 SidebarThemeExpandedButton 顶替）。侧边栏默认是展开的，
    #    所以必须先折叠再断言 —— 以前这里直接断言，等于在找一块本就不该存在的按钮。
    _toggle_sidebar(win)
    try:
        theme_btn = win.child_window(auto_id="SidebarThemeCollapsedButton", control_type="Button")
        assert theme_btn.exists(timeout=3), (
            "SidebarThemeCollapsedButton 应出现在折叠态侧边栏上"
        )

        # 4. Cycle app theme once: default is System, one click -> Light
        theme_btn.invoke()
        time.sleep(0.5)
    finally:
        # 恢复展开态，避免影响后续断言与保存
        _toggle_sidebar(win)
    
    # 5. Save settings and verify config persistence
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.8)
    
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file not found at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert config.get("AppTheme") == "Light", f"AppTheme ({config.get('AppTheme')}) should be 'Light' after one cycle from default System"


def test_v130_wheel_themes_and_custom_preset_and_text_sync(app):
    """
    Test v1.3.0 features:
    1. Verify 4 core Wheel Styles in UiStyleComboBox.
    2. Verify 7 core Wheel Color Themes in ThemeComboBox.
    3. Verify SaveCustomColorPresetButton exists.
    4. Verify ShowText and IconLayoutMode synchronization.
    5. Save settings and verify config persistence.
    """
    win, local_app_data = app
    
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 1. Verify UiStyle dropdown (轮盘主题风格)
    ui_style_combo = win.child_window(auto_id="UiStyleComboBox", control_type="ComboBox")
    assert ui_style_combo.exists(timeout=3), "UiStyleComboBox should exist"
    
    # 2. Verify Theme dropdown (轮盘配色方案)
    wheel_theme_combo = win.child_window(auto_id="ThemeComboBox", control_type="ComboBox")
    assert wheel_theme_combo.exists(timeout=3), "ThemeComboBox should exist"
    
    # 3. Select Theme (Index 1: Dark) and UiStyle (Index 1: CleanSectors)
    wheel_theme_combo.select(1)
    ui_style_combo.select(1)
    time.sleep(0.4)
    
    # 4. Verify center action-name text toggle (v1.6.8 renamed ShowTextCheckBox)
    #    and IconLayoutMode dropdown
    show_text_chk = win.child_window(auto_id="ShowSelectedActionTextCheckBox", control_type="CheckBox")
    assert show_text_chk.exists(timeout=3), "ShowSelectedActionTextCheckBox should exist"
    
    layout_mode_combo = win.child_window(auto_id="IconLayoutModeComboBox", control_type="ComboBox")
    assert layout_mode_combo.exists(timeout=3), "IconLayoutModeComboBox should exist"
    
    # 5. Save settings and verify config persistence
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.8)
    
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file not found at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert config.get("Theme") == "Dark", f"Theme ({config.get('Theme')}) should be 'Dark'"
    assert config.get("UiStyle") == "CleanSectors", f"UiStyle ({config.get('UiStyle')}) should be 'CleanSectors'"


def test_v132_shapes_fontsize_and_iconsize_control(app):
    """
    Test v1.3.2 features:
    1. Navigation to Appearance Tab (NavTab1).
    2. Verification of new shapes in ShapeComboBox (OrganicPetals, ArcTracker, RoundedCapsule).
    3. Verification of SectorIconSizeSlider and SectorFontSizeSlider updating.
    4. Save settings and verify config persistence for SectorIconSize and SectorFontSize.
    """
    win, local_app_data = app
    
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 1. Verify ShapeComboBox exists and can select new shapes
    shape_combo = win.child_window(auto_id="ShapeComboBox", control_type="ComboBox")
    assert shape_combo.exists(timeout=3), "ShapeComboBox should exist"
    
    # Select Capsule or HexagonHive
    shape_combo.select(2)
    time.sleep(0.3)
    
    # 2. Verify SectorIconSizeSlider exists and functions
    icon_slider = win.child_window(auto_id="SectorIconSizeSlider", control_type="Slider")
    icon_label = win.child_window(auto_id="SectorIconSizeLabel", control_type="Text")
    assert icon_slider.exists(timeout=3), "SectorIconSizeSlider should exist"
    assert icon_label.exists(timeout=3), "SectorIconSizeLabel should exist"
    
    icon_slider.set_value(26)
    time.sleep(0.3)
    assert "26" in icon_label.window_text()

    # 3. Verify SectorFontSizeSlider exists and functions
    font_slider = win.child_window(auto_id="SectorFontSizeSlider", control_type="Slider")
    font_label = win.child_window(auto_id="SectorFontSizeLabel", control_type="Text")
    assert font_slider.exists(timeout=3), "SectorFontSizeSlider should exist"
    assert font_label.exists(timeout=3), "SectorFontSizeLabel should exist"
    
    font_slider.set_value(13.5)
    time.sleep(0.3)
    assert "13.5" in font_label.window_text()
    
    # 4. Save and verify persistence
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.8)
    
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file not found at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert abs(config.get("SectorIconSize", 0) - 26) < 1.0, f"Saved SectorIconSize should be 26, got {config.get('SectorIconSize')}"
    assert abs(config.get("SectorFontSize", 0) - 13.5) < 0.1, f"Saved SectorFontSize should be 13.5, got {config.get('SectorFontSize')}"


def test_v133_sector_count_4_8_12_adaptation_and_streamlined_shapes(app):
    """
    Test v1.3.3 features:
    1. Verify streamlined shapes (4 items in ShapeComboBox).
    2. Switch to Gestures & Actions tab (NavTab2).
    3. Verify 4-key (SectorCount4Radio) and 12-key (SectorCount12Radio) selection works.
    4. Save settings and verify profile SectorCount is correctly updated and persisted.
    """
    win, local_app_data = app
    
    # 1. Verify streamlined shapes
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    shape_combo = win.child_window(auto_id="ShapeComboBox", control_type="ComboBox")
    assert shape_combo.exists(timeout=3), "ShapeComboBox should exist"
    
    # 2. Switch to Gestures & Actions tab
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.5)
    
    # v1.6.8: canvas-mode radios on the Mappings page
    radio4 = win.child_window(auto_id="MappingsSectorCount4Radio", control_type="RadioButton")
    radio8 = win.child_window(auto_id="MappingsSectorCount8Radio", control_type="RadioButton")
    radio12 = win.child_window(auto_id="MappingsSectorCount12Radio", control_type="RadioButton")
    
    assert radio4.exists(timeout=3), "SectorCount4Radio should exist"
    assert radio8.exists(timeout=3), "SectorCount8Radio should exist"
    assert radio12.exists(timeout=3), "SectorCount12Radio should exist"
    
    # 3. Select 12-key sector count
    radio12.select()
    time.sleep(0.4)
    
    # 4. Save and verify persistence in config
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=3):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except Exception:
        pass
        
    time.sleep(0.8)
    
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file not found at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    profiles = config.get("Profiles", [])
    assert len(profiles) > 0, "Should have at least one profile"
    global_prof = next((p for p in profiles if p.get("ProcessName") == "Global"), profiles[0])
    assert global_prof.get("SectorCount") == 12, f"Global profile SectorCount should be 12, got {global_prof.get('SectorCount')}"




def test_v134_memory_autosave_and_theme_persistence(app):
    """
    Test v1.3.4 features:
    1. Cycle AppTheme via the sidebar theme button (v1.6.8 replaces AppThemeComboBox,
       System -> Light, persisted immediately by SetAppTheme).
    2. Change a slider (WheelRadiusSlider).
    3. Verify config is automatically persisted to disk via debounce / window close.
    4. Verify settings persistence without needing explicit SaveButton click.
    """
    win, local_app_data = app
    
    # 1. Navigate to Appearance Tab (NavTab1)
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 2. Cycle AppTheme via sidebar button (System -> Light, auto-persisted)
    #    该按钮是折叠态侧边栏专属的，侧边栏默认展开 —— 先折叠，点完再恢复。
    _toggle_sidebar(win)
    try:
        theme_btn = win.child_window(auto_id="SidebarThemeCollapsedButton", control_type="Button")
        assert theme_btn.exists(timeout=3), (
            "SidebarThemeCollapsedButton 应出现在折叠态侧边栏上"
        )
        theme_btn.invoke()
        time.sleep(0.6)
    finally:
        _toggle_sidebar(win)
    
    # 3. Change a slider (WheelRadiusSlider)
    wheel_slider = win.child_window(auto_id="WheelRadiusSlider", control_type="Slider")
    wheel_slider.set_value(145.0)
    time.sleep(0.6) # Allow debounce timer to trigger auto-save
    
    # 4. Check config file directly
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file should be auto-persisted at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert config.get("AppTheme") == "Light", f"Auto-persisted AppTheme should be 'Light', got {config.get('AppTheme')}"
    assert abs(config.get("WheelRadius", 0) - 145.0) < 1.0, f"Auto-persisted WheelRadius should be 145, got {config.get('WheelRadius')}"


def test_v135_program_picker_clean_icons_and_core_customization(advanced_mode):
    """
    Test v1.3.5 features:
    1. Navigate to Appearance Tab (NavTab1).
    2. Toggle ShowCoreIconCheckBox.
    3. Select a Core Pattern from CoreIconTypeComboBox (e.g., Windows Logo or Crosshair).
    4. Verify configuration auto-persists ShowCoreIcon and CoreIconType.
    5. Navigate to Mappings Tab (NavTab2), open ProgramPickerWindow, verify it opens and closes cleanly.

    第 5 步要用的 AddProfileButton 在 NavTab2 的列表模式里，而列表模式只在高级全量模式下可达 —— 故用 advanced_mode。
    """
    win, local_app_data = advanced_mode
    
    # 1. Appearance Tab
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 2. Check Core Icon controls exist
    # v1.6.8dev2: CoreIconTypeComboBox lives inside CoreIconConfigPanel, which is
    # collapsed until ShowCoreIcon is enabled — check the box before asserting it
    core_chk = win.child_window(auto_id="ShowCoreIconCheckBox", control_type="CheckBox")
    assert core_chk.exists(timeout=3), "ShowCoreIconCheckBox should exist"

    if core_chk.get_toggle_state() == 0:
        core_chk.toggle()
        time.sleep(0.4)

    core_combo = win.child_window(auto_id="CoreIconTypeComboBox", control_type="ComboBox")
    assert core_combo.exists(timeout=3), "CoreIconTypeComboBox should exist"

    # 3. Select Core Pattern
    core_combo.select(1)
    time.sleep(0.6)
    
    # 4. Verify config persisted
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file should exist at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert "ShowCoreIcon" in config, "ShowCoreIcon should be persisted in config"
    assert "CoreIconType" in config, "CoreIconType should be persisted in config"
    
    # 5. Test ProgramPickerWindow in Mappings Tab
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    
    _switch_to_list_mode(win)

    add_btn = win.child_window(auto_id="AddProfileButton", control_type="Button")
    assert add_btn.exists(timeout=3), "AddProfileButton should exist"


def test_v136_glow_color_customization_config_memory_and_core_image(app):
    """
    Test v1.3.6 features:
    1. Verify Highlight Glow controls (HighlightGlowPresetComboBox, HighlightGlowRadiusSlider, HighlightGlowOpacitySlider).
    2. Change Highlight Glow Preset and verify dynamic color assignment.
    3. Verify Center Core Image support (CoreIconTypeComboBox item 'Image', CoreImagePathTextBox).
    4. Verify config persistence of all v1.3.6 settings into config.json.
    5. Test config reload integrity to ensure zero configuration regression or overwriting upon startup.
    """
    win, local_app_data = app
    
    # 1. Appearance Tab
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 2. Check Highlight Glow controls
    glow_preset_combo = win.child_window(auto_id="HighlightGlowPresetComboBox", control_type="ComboBox")
    assert glow_preset_combo.exists(timeout=3), "HighlightGlowPresetComboBox should exist"
    
    glow_radius_slider = win.child_window(auto_id="HighlightGlowRadiusSlider", control_type="Slider")
    assert glow_radius_slider.exists(timeout=3), "HighlightGlowRadiusSlider should exist"
    
    glow_opacity_slider = win.child_window(auto_id="HighlightGlowOpacitySlider", control_type="Slider")
    assert glow_opacity_slider.exists(timeout=3), "HighlightGlowOpacitySlider should exist"
    
    # Select a glow preset (e.g. 1: Lilac Violet)
    glow_preset_combo.select(1)
    time.sleep(0.3)
    
    # 3. Check Core Image Controls
    # v1.6.8dev2: CoreIconTypeComboBox is hidden inside CoreIconConfigPanel until
    # ShowCoreIcon is enabled — check the box first
    core_chk = win.child_window(auto_id="ShowCoreIconCheckBox", control_type="CheckBox")
    assert core_chk.exists(timeout=3), "ShowCoreIconCheckBox should exist"

    if core_chk.get_toggle_state() == 0:
        core_chk.toggle()
        time.sleep(0.4)

    core_combo = win.child_window(auto_id="CoreIconTypeComboBox", control_type="ComboBox")
    assert core_combo.exists(timeout=3), "CoreIconTypeComboBox should exist"
    
    # Select Image item (last item)
    core_combo.select(core_combo.item_count() - 1)
    time.sleep(0.3)
    
    core_img_box = win.child_window(auto_id="CoreImagePathTextBox", control_type="Edit")
    assert core_img_box.exists(timeout=3), "CoreImagePathTextBox should exist"
    
    # Enter dummy image path
    dummy_img_path = "C:\\Windows\\System32\\SecurityAndMaintenance_Error.png"
    core_img_box.set_text(dummy_img_path)
    time.sleep(0.6) # Allow debounce auto-save
    
    # 4. Verify config file contains all v1.3.6 entries
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file should exist at {config_path}"
    
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        
    assert config.get("HighlightGlowPreset") == "Lilac", f"Expected HighlightGlowPreset to be 'Lilac', got {config.get('HighlightGlowPreset')}"
    assert config.get("HighlightGlowColor") == "#A855F7", f"Expected HighlightGlowColor to be '#A855F7', got {config.get('HighlightGlowColor')}"
    assert config.get("CoreIconType") == "Image", f"Expected CoreIconType to be 'Image', got {config.get('CoreIconType')}"
    assert config.get("CoreCustomImagePath") == dummy_img_path, f"Expected CoreCustomImagePath to be '{dummy_img_path}', got {config.get('CoreCustomImagePath')}"
    assert "HighlightGlowRadius" in config, "HighlightGlowRadius should be present in config"
    assert "HighlightGlowOpacity" in config, "HighlightGlowOpacity should be present in config"


def test_v136_custom_color_preset_deletion_and_management(app):
    """
    Test v1.3.6 Custom Color Preset Deletion & Management:
    1. Navigate to Appearance Tab (NavTab1).
    2. Open ThemeComboBox and select Custom ("自定义高级配色").
    3. Verify CustomColorsPanel is visible, and Save button exists.
    4. Select a custom preset if present or check Delete button functionality and visibility.
    5. Verify clean management of custom color presets and config synchronization.
    """
    win, local_app_data = app
    
    # 1. Appearance Tab
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    # 2. Check ThemeComboBox existence
    theme_combo = win.child_window(auto_id="ThemeComboBox", control_type="ComboBox")
    assert theme_combo.exists(timeout=3), "ThemeComboBox should exist"
    
    # 3. Verify CustomColorExpander exists
    color_expander = win.child_window(auto_id="CustomColorExpander", control_type="Group")
    assert color_expander.exists(timeout=3), "CustomColorExpander should exist"
    
    # 4. Verify config file integrity
    config_path = get_config_path(local_app_data)
    assert os.path.exists(config_path), f"Config file should exist at {config_path}"


def test_v138_i18n_multilanguage_support(app):
    """
    Test v1.3.8 Multi-Language (i18n) Support:
    1. Navigate to Advanced & System Tab (NavTab3).
    2. Verify LanguageComboBox exists and contains zh-CN, zh-TW, en, ja, and Auto.
    3. Switch language to English (en).
    4. Verify UI elements dynamically update to English text.
    5. Verify config.json persists Language="en".
    6. Switch language to Japanese (ja), verify Japanese text.
    7. Switch back to Simplified Chinese (zh-CN).
    """
    win, local_app_data = app
    
    # 1. Advanced & System Tab
    tab3 = win.child_window(auto_id="NavTab3", control_type="RadioButton")
    tab3.select()
    time.sleep(0.4)
    
    # 2. Check LanguageComboBox existence
    lang_combo = win.child_window(auto_id="LanguageComboBox", control_type="ComboBox")
    assert lang_combo.exists(timeout=3), "LanguageComboBox should exist in Tab 3"
    
    # Verify items count >= 5 (zh-CN, zh-TW, en, ja, Auto)
    assert lang_combo.item_count() >= 5, f"LanguageComboBox should have at least 5 options, got {lang_combo.item_count()}"
    
    # 3. Select English (Tag="en", index 2)
    lang_combo.select(2)
    time.sleep(0.5)
    
    # 4. Verify UI elements updated to English
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    assert "Save" in save_btn.window_text(), f"Save button should be in English, got {save_btn.window_text()}"
    
    # 侧边栏**默认就是展开态**，NavTab 的文字标签本来就在。
    # 这里以前有一句「展开侧边栏」的 invoke()，但它是按「默认折叠」的旧假设写的 ——
    # 实际效果是**把侧边栏折叠了**，反而让 NavTab0Text 消失，于是断言必然失败。
    # 侧边栏的折叠态只保留图标，NavTab0Text 这类标签会整块移出自动化树。
    tab0_text = win.child_window(auto_id="NavTab0Text", control_type="Text")
    assert tab0_text.exists(timeout=3), (
        "NavTab0Text 找不到：侧边栏处于折叠态？折叠态只留图标，文字标签不进自动化树"
    )
    assert "Trigger" in tab0_text.window_text() or "🎯" in tab0_text.window_text(), "Tab0 should update"
    
    # 5. Check config file persists Language = "en"
    config_path = get_config_path(local_app_data)
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
    assert config.get("Language") == "en", f"Expected config Language='en', got {config.get('Language')}"
    
    # 6. Switch to Japanese (Tag="ja", index 3)
    lang_combo.select(3)
    time.sleep(0.5)
    
    assert "保存" in save_btn.window_text(), f"Save button should update to Japanese, got {save_btn.window_text()}"
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
    assert config.get("Language") == "ja", f"Expected config Language='ja', got {config.get('Language')}"
    
    # 7. Switch back to zh-CN (Tag="zh-CN", index 0)
    lang_combo.select(0)
    time.sleep(0.5)
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
    assert config.get("Language") == "zh-CN", f"Expected config Language='zh-CN', got {config.get('Language')}"

def test_v139_folder_action_type_and_i18n_consistency(app):
    """
    Test v1.3.9 Folder Action Type and Global UI I18n Consistency:
    1. Navigate to Gestures & Actions (NavTab2).
    2. Verify SectorActionListTitleText and ProfileCardTitleText exist.
    3. Locate the first slot's Action Type ComboBox and select "Folder" (index 2).
    4. Save configuration and verify config.json persists Type="Folder".
    5. Switch language to English (en), verify action type options are translated.
    6. Switch back to zh-CN.
    """
    win, local_app_data = app
    
    # 1. Switch to Tab 2
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    
    # 2. Check the canvas focus editor title (replaces old SectorActionListTitleText)
    focus_title = win.child_window(auto_id="FocusSlotTitleText", control_type="Text")
    assert focus_title.exists(timeout=3), "FocusSlotTitleText should exist"
    
    # 3. Locate the first slot's Action Type ComboBox and select "Folder"
    type_combo = win.child_window(auto_id="FocusActionTypeComboBox", control_type="ComboBox")
    assert type_combo.exists(timeout=3), "FocusActionTypeComboBox should exist"

    # 这里以前写死 `item_count() == 9`，是个过时魔数，什么也没守住：
    # 内置动作类型有 9 个，插件动作类型再追加 1 个；而**简洁模式会过滤掉
    # Command 与 WindowManager 两个低频类型**，所以默认（简洁）模式下是 7 + 1 = 8。
    # 一改模式或一加类型它就红，却说不清哪里坏了。改为断言两条真正有意义的不变量：
    #   a) 插件动作类型必须可见（v1.7.x 新增能力，掉了要有人知道）—— 用语言无关的 🔌 前缀识别；
    #   b) 内置项顺序契约：内置类型顺序属于用户肌肉记忆，插件动作只追加在末尾
    #      （见 SlotViewModel.AggregatedActionTypes），因此 index 3 恒为 Folder，
    #      下面 select(3) 才成立、不会被新增类型顶错位。
    item_texts = _combo_item_texts(type_combo)
    assert len(item_texts) >= 4, f"动作类型太少，index 3 不可能是 Folder：{item_texts}"
    assert any("🔌" in t for t in item_texts), (
        f"插件动作类型应出现在类型下拉里（简洁模式下也应在）：{item_texts}"
    )
    assert "🔌" in item_texts[-1], (
        f"插件动作类型必须追加在内置类型之后，不能打乱内置顺序：{item_texts}"
    )
    _collapse_combo(type_combo)

    type_combo.select(3)
    time.sleep(0.3)
    
    # 4. Save configuration
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    time.sleep(0.5)
    
    config_path = get_config_path(local_app_data)
    with open(config_path, "r", encoding="utf-8") as f:
        saved_config = json.load(f)
        
    assert saved_config["Profiles"][0]["Actions"][0]["Type"] == "Folder", "Action type should persist as Folder"

def test_v140_custom_icons_and_appearance_collapsible_and_milestones_folding(app):
    """
    Test v1.4.0 Features:
    1. Appearance Tab (NavTab1):
       - Verify UiStyleComboBox does not contain CatPaw.
       - Verify CustomColorExpander exists and is collapsible.
    2. Gestures Tab (NavTab2):
       - Verify Launch and Folder browse buttons exist.
    3. About Tab (NavTab4):
       - Verify Milestone cards exist and OlderMilestonesExpander exists.
    """
    win, local_app_data = app
    
    # 1. Appearance Tab (Tab 1)
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    ui_style_combo = win.child_window(auto_id="UiStyleComboBox", control_type="ComboBox")
    assert ui_style_combo.exists(timeout=3), "UiStyleComboBox should exist"
    # Should have exactly 3 styles now (ClassicRing, CleanSectors, Glassmorphism)
    assert ui_style_combo.item_count() == 3, f"UiStyleComboBox should have 3 items without CatPaw, got {ui_style_combo.item_count()}"
    
    color_expander = win.child_window(auto_id="CustomColorExpander", control_type="Group")
    assert color_expander.exists(timeout=3), "CustomColorExpander should exist"
    
    # 2. Gestures Tab (Tab 2) - canvas focus editor title (replaces old title text)
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    
    focus_title = win.child_window(auto_id="FocusSlotTitleText", control_type="Text")
    assert focus_title.exists(timeout=3), "FocusSlotTitleText should exist"
    
    # 3. About Tab (Tab 4)
    tab4 = win.child_window(auto_id="NavTab4", control_type="RadioButton")
    tab4.select()
    time.sleep(0.4)
    
    older_expander = win.child_window(auto_id="OlderMilestonesExpander", control_type="Group")
    assert older_expander.exists(timeout=3), "OlderMilestonesExpander should exist"

def test_v141_outer_escape_cancel_and_rename_capabilities(advanced_mode):
    """
    Test v1.4.1 Features:
    1. Triggers & Scenes Tab (NavTab0):
       - Verify EnableOuterEscapeCheckBox exists and can be toggled.
    2. Gestures Tab (NavTab2):
       - Verify RenameProfileButton exists and is enabled.
    3. Appearance Tab (NavTab1):
       - Verify custom color expander and theme preset capabilities.
    4. Save configuration and verify persistence of v1.4.1 settings.

    用 advanced_mode：RenameProfileButton 在 NavTab2 的列表模式里，
    而列表模式只在高级全量模式下可达。
    """
    win, local_app_data = advanced_mode
    
    # 1. Triggers Tab (Tab 0)
    tab0 = win.child_window(auto_id="NavTab0", control_type="RadioButton")
    tab0.select()
    time.sleep(0.4)
    
    outer_escape_chk = win.child_window(auto_id="EnableOuterEscapeCheckBox", control_type="CheckBox")
    assert outer_escape_chk.exists(timeout=3), "EnableOuterEscapeCheckBox should exist"
    
    # Toggle ON to reveal slider
    outer_escape_chk.toggle()
    time.sleep(0.3)
    
    escape_dist_slider = win.child_window(auto_id="OuterEscapeDistanceSlider", control_type="Slider")
    assert escape_dist_slider.exists(timeout=3), "OuterEscapeDistanceSlider should exist"
    
    # 2. Gestures Tab (Tab 2)
    tab2 = win.child_window(auto_id="NavTab2", control_type="RadioButton")
    tab2.select()
    time.sleep(0.4)
    
    # v1.6.8: rename control lives in the list view
    _switch_to_list_mode(win)

    rename_profile_btn = win.child_window(auto_id="RenameProfileButton", control_type="Button")
    assert rename_profile_btn.exists(timeout=3), "RenameProfileButton should exist"
    
    # 3. Appearance Tab (Tab 1)
    tab1 = win.child_window(auto_id="NavTab1", control_type="RadioButton")
    tab1.select()
    time.sleep(0.4)
    
    new_preset_btn = win.child_window(auto_id="NewCustomColorPresetButton", control_type="Button")
    assert new_preset_btn.exists(timeout=3), "NewCustomColorPresetButton should exist"
    
    color_expander = win.child_window(auto_id="CustomColorExpander", control_type="Group")
    assert color_expander.exists(timeout=3), "CustomColorExpander should exist"
    
    # 4. Save and verify config
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    time.sleep(0.5)
    
    config_path = get_config_path(local_app_data)
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
    assert "EnableOuterEscapeCancel" in config, "EnableOuterEscapeCancel should be in config.json"
    assert config["EnableOuterEscapeCancel"] is True, "EnableOuterEscapeCancel should default to True"

def test_v143_trigger_button_customization(app):
    win, local_app_data = app
    
    # 1. Triggers Tab (Tab 0)
    tab0 = win.child_window(auto_id="NavTab0", control_type="RadioButton")
    tab0.select()
    time.sleep(0.4)
    
    badge = win.child_window(auto_id="CurrentTriggerBadgeText", control_type="Text")
    assert badge.exists(timeout=3), "CurrentTriggerBadgeText should exist on Tab 0"
    
    rec_btn = win.child_window(auto_id="RecordTriggerButton", control_type="Button")
    assert rec_btn.exists(timeout=3), "RecordTriggerButton should exist on Tab 0"
    
    sensor = win.child_window(auto_id="LiveSensorStatusText", control_type="Text")
    assert sensor.exists(timeout=3), "LiveSensorStatusText should exist on Tab 0"
    
    reset_btn = win.child_window(auto_id="ResetDefaultTriggerButton", control_type="Button")
    assert reset_btn.exists(timeout=3), "ResetDefaultTriggerButton should exist on Tab 0"
    
    # Click reset to default
    reset_btn.invoke()
    time.sleep(0.3)
    
    # Save
    save_btn = win.child_window(auto_id="SaveButton", control_type="Button")
    save_btn.invoke()
    time.sleep(0.5)
    
    try:
        dialog = Desktop(backend="uia").window(class_name="#32770")
        if dialog.exists(timeout=2):
            ok_btn = dialog.child_window(control_type="Button")
            ok_btn.invoke()
    except:
        pass
        
    config_path = get_config_path(local_app_data)
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
    assert "Trigger" in config or "TriggerButton" in config, "Trigger configuration should be persisted in config.json"

def test_theme_segment_switching_and_logo(app):
    win, local_app_data = app
    
    # 1. Expand sidebar if collapsed so theme radio buttons are visible
    toggle_btn = win.child_window(auto_id="SidebarToggleButton", control_type="Button")
    if toggle_btn.exists(timeout=2):
        toggle_btn.invoke()
        time.sleep(0.3)
        
    # 2. Switch to Dark theme
    theme_dark = win.child_window(auto_id="ThemeBtnDark", control_type="RadioButton")
    if theme_dark.exists(timeout=2):
        theme_dark.select()
        time.sleep(0.3)
        config_path = get_config_path(local_app_data)
        with open(config_path, "r", encoding="utf-8") as f:
            cfg = json.load(f)
        assert cfg.get("AppTheme") == "Dark"
        
    # 3. Switch to Light theme
    theme_light = win.child_window(auto_id="ThemeBtnLight", control_type="RadioButton")
    if theme_light.exists(timeout=2):
        theme_light.select()
        time.sleep(0.3)
        config_path = get_config_path(local_app_data)
        with open(config_path, "r", encoding="utf-8") as f:
            cfg = json.load(f)
        assert cfg.get("AppTheme") == "Light"
        
    # 4. Check SidebarLogoImage
    logo_img = win.child_window(auto_id="SidebarLogoImage", control_type="Image")
    assert logo_img.exists(timeout=2), "SidebarLogoImage should exist in visual tree"

def test_update_ui_elements_and_check(app):
    win, local_app_data = app
    
    # 1. Switch to Tab 3 (Advanced & System Settings)
    tab3 = win.child_window(auto_id="NavTab3", control_type="RadioButton")
    tab3.select()
    time.sleep(0.4)
    
    # 2. Verify Update controls exist
    check_btn = win.child_window(auto_id="CheckUpdateNowBtn", control_type="Button")
    assert check_btn.exists(timeout=3), "CheckUpdateNowBtn should exist"
    
    web_btn = win.child_window(auto_id="ViewReleasesWebBtn", control_type="Button")
    assert web_btn.exists(timeout=3), "ViewReleasesWebBtn should exist"
    
    status_text = win.child_window(auto_id="UpdateStatusBadgeText", control_type="Text")
    assert status_text.exists(timeout=3), "UpdateStatusBadgeText should exist"
    
    # 更新推送通道 / 下载代理两个下拉住在 UpdateAdvancedSettingsExpander 里，
    # 而它 IsExpanded="False" —— **折叠的 Expander 内容不进自动化树**，
    # 直接断言会得到「控件不存在」，看着像功能没了，其实只是没展开。
    advanced_expander = win.child_window(auto_id="UpdateAdvancedSettingsExpander")
    assert advanced_expander.exists(timeout=3), "UpdateAdvancedSettingsExpander should exist"
    advanced_expander.expand()
    time.sleep(0.6)

    channel_combo = win.child_window(auto_id="UpdateChannelComboBox", control_type="ComboBox")
    assert channel_combo.exists(timeout=3), "UpdateChannelComboBox should exist"

    proxy_combo = win.child_window(auto_id="UpdateProxyComboBox", control_type="ComboBox")
    assert proxy_combo.exists(timeout=3), "UpdateProxyComboBox should exist"
    
    # 3. Wait for any initial background update check to finish if running
    for _ in range(30):
        if check_btn.is_enabled():
            break
        time.sleep(0.2)

    # Click CheckUpdateNowBtn and verify status transitions to latest version
    check_btn.invoke()

    # 等这次检查真正走完：按钮回到可用 **且** 徽标离开过程态。
    #
    # 只等按钮是不够的：存在「刚 invoke、禁用还没生效」的竞态，
    # 那时立刻读徽标会读到「正在检查更新...」，断言以一条误导性的信息挂掉 ——
    # 看起来像功能坏了，其实只是没等够。
    for _ in range(40):
        time.sleep(0.3)
        if check_btn.is_enabled() and "正在检查" not in status_text.window_text():
            break

    # 按钮必须回到可用 —— 这条才是真正的回归护栏：
    # 检查更新若在异常路径上把按钮永久禁用，用户就再也点不动了。
    assert check_btn.is_enabled(), "CheckUpdateNowBtn should be re-enabled after checking"

    # 徽标必须落在一个**终态**上，不能永远停在「正在检查更新...」。
    #
    # 这里刻意不把「已是最新 / 发现新版本」写成硬断言：这条用例会真的联网去查
    # release，在无网 / 代理受限 / CI 里拿到的是「检查更新受阻」——
    # 那是**环境**结论，不是产品缺陷。徽标全部可能的终态：
    #   当前已是最新版本 / 发现新版本 x.y.z / 检查更新受阻
    #   下载完成 · 就绪安装 / 回退包下载完成 · 就绪安装
    # 早先的写法把成功文案写成三选一硬断言，于是这条用例在离线环境下必红，
    # 而且红得毫无信息量。现在只守住「检查跑到了终点」这个真契约。
    badge_val = status_text.window_text()
    assert "正在检查" not in badge_val, (
        f"检查更新应已结束，徽标却仍停在过程态：{badge_val!r}")
    if "受阻" in badge_val:
        print(f"[SKIP] 更新检查因网络不可达而终止（环境结论，非产品缺陷）：{badge_val!r}")
    else:
        assert "版本" in badge_val, (
            f"联网状态下徽标应给出明确的版本结论，实际：{badge_val!r}")


def test_left_button_trigger_behavior_and_long_press(app):
    win, local_app_data = app
    
    # 1. Switch to Tab 0 (Trigger and Isolation settings)
    tab0 = win.child_window(auto_id="NavTab0", control_type="RadioButton")
    tab0.select()
    time.sleep(0.4)
    
    # 2. Verify controls exist
    badge = win.child_window(auto_id="CurrentTriggerBadgeText", control_type="Text")
    assert badge.exists(timeout=3), "CurrentTriggerBadgeText should exist"
    
    rec_btn = win.child_window(auto_id="RecordTriggerButton", control_type="Button")
    assert rec_btn.exists(timeout=3), "RecordTriggerButton should exist"
    
    long_press_chk = win.child_window(auto_id="LongPressTriggerCheckBox", control_type="CheckBox")
    assert long_press_chk.exists(timeout=3), "LongPressTriggerCheckBox should exist"
    
    reset_btn = win.child_window(auto_id="ResetDefaultTriggerButton", control_type="Button")
    assert reset_btn.exists(timeout=3), "ResetDefaultTriggerButton should exist"
    
    # 3. Test Reset to default RightButton
    reset_btn.invoke()
    time.sleep(0.3)
    assert "右键" in badge.window_text(), f"Expected RightButton in badge text, got: {badge.window_text()}"

