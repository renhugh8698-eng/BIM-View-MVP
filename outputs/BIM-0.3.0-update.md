# BIM 0.3.0 更新与验证

## 材质问题

检查 `C:\MyChatFloder\BimViewr\test\model.glb`：生成器为 0.2.0，445 个构件，只有两种灰色材质，全部不透明。原颜色并未写进 GLB，因此仅修改网页灯光无法恢复。

0.3.0 改为读取 Revit 指定材质的着色颜色、透明度、名称与身份；无指定材质时使用导出回调颜色。此策略有意避免把单色视图的颜色当成原始材质，不保留视图图形替换。光泽度近似转换为粗糙度。

这是着色材质支持，不是完整真实感材质：纹理图片、程序纹理、金属度和折射外观尚未导出。需要使用新插件重新导出，旧文件不会自动恢复材质。

## AddInManager 加载

交付 DLL：

```text
C:\Users\Admin\Documents\Codex\2026-10-14\referenced-chatgpt-conversation-this-is-an\outputs\BIMExporter-0.3.0\BIMExporter.dll
```

构建原位置：

```text
C:\Users\Admin\Documents\Codex\2026-10-14\referenced-chatgpt-conversation-this-is-an\BIMExporter\bin\Release\0.3.0\net48\BIMExporter.dll
```

在 AddInManager 的 Manual 模式选择 DLL，并运行 `BIMExporter.Commands.ExportModelCommand`。不要选仅负责创建功能区按钮的 `BIMExporter.App`。确认导出完成弹窗显示 0.3.0，再用新 GLB 查看颜色和玻璃透明度。

本次未更改原功能区插件清单。原按钮仍可能调用 0.2.0；通过 AddInManager 直接执行新命令即可避免混用。是否支持同一会话反复加载取决于已安装的 AddInManager，未进行实机加载验证。后续发布继续使用独立版本目录并提供 DLL 路径。

## 网页更新

参考 [minBIM 示例](https://www.minbim.com/examples/viewer/id/kL8xP_2mNq.html) 与 [BIMFACE 示例](https://api.bimface.com/preview/32704cbf?viewid=1369)，选择可在现有 Three.js 架构内落地的部分：

- 大画布、可收起侧栏、中文属性面板和构件搜索。
- 原始材质、分类着色、白模、透视四种模式；分类颜色带图例，明确区别于原材质。
- 标准旋转、平移、缩放、选中构件定位、全模型居中，以及顶/前/右视角。
- X/Y/Z 单平面剖切和位置滑块；剖切不封口，拾取忽略被裁掉的表面。
- 按需构件边线、地面网格、亮度调节、环境光照与透明材质深度处理。
- 模型/相机变化时才提交渲染；边线按需生成，大模型开启边线仍有额外成本。

运行地址：<http://127.0.0.1:5173/>。刷新即可使用网页新版。

## 验证记录

- Revit 2020 API 下插件构建通过，零警告、零错误。
- C# 写入器测试通过，新增材质名称、身份、粗糙度、颜色和透明度检查。
- 前端生产构建通过，16 项测试通过（含实际导出文件和 C# 合成 GLB）。
- 浏览器使用实际 445 构件、410,128 三角形模型，验证单文件加载、搜索、属性联动、定位、隐藏、隔离、视角、边线、分类着色和剖切。
- 390×844 窄屏无横向溢出，画布非空白，旋转和属性页正常。浏览器未记录警告或错误。
- 另用合成透明玻璃验证原始材质渲染链路；这不能替代 Revit 内新版导出验证。

真实材质修复和 AddInManager 加载仍需在 Revit 2020 中验收：检查彩色材质、玻璃、同色不同材质、未分配材质和链接模型。建议新旧版本分别导出到不同目录比较。

构建仍有 Three.js 主包体积提示；依赖审计报告 6 项漏洞，本次未进行可能破坏兼容性的依赖升级。此版本仍定位为本地 MVP，不是面向公网部署的安全验收版本。
