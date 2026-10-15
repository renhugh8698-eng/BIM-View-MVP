# BIM MVP 0.1.1 更新

## 门窗错位

修复重复应用族实例变换的问题。`GetInstanceGeometry()` 已返回经过实例放置的几何，遍历时不再重复乘 `instance.Transform`；米制单位转换与 Z-up 到 Y-up 转换仍保留。补上直接 Mesh 几何的读取。

已查看 [Revit2Gltf 的导出入口](https://github.com/twinup/Revit2Gltf/blob/main/Revit2Gltf.Plugin/Commands/ExportToGltfCommand.cs)：其实际路线是 Revit 原生 FBX 导出，再经 Assimp 转成 glTF。当前修复保留直接 GLB/JSON 路线，没有引入 FBX 中间文件或 Assimp 依赖。

新版 DLL 已编译并注册到 `BIMExporter/bin/Release/net48/BIMExporter.dll`，版本为 0.1.1.0。旧 DLL 正被 Revit 使用，因此保留旧文件，修改注册指向新目录。以后关闭 Revit 后正常 Release 编译即可更新该固定路径。

**请保存工程并关闭所有 Revit 2020 实例，再启动 Revit 重新导出到新文件夹。** 新 JSON 中应有 `exporterVersion: "0.1.1"`。旧 GLB 已包含错误坐标，浏览器无法可靠还原。

## GLB/JSON 加载

- “打开模型”可一次选择 JSON 和 GLB，也可分两次选择，等两个文件齐全后再加载。
- “打开文件夹”读取文件夹中的一份 JSON 和一份 GLB；多份同类型文件会明确报错，避免猜错配对。
- 兼容旧 JSON 的 PascalCase 字段；新导出统一为 camelCase。
- 加载前验证 GLB 头和构件 ID 集合。仅 ID 相同不能证明是同一次几何修订，仍应使用同次导出的文件。
- 修复模型树崩溃、属性索引失效、半屏画布、窄窗口属性入口，以及加载回调过时和部分资源未释放的问题。

## 已验证与待验证

- Revit 2020 API 编译成功，0 警告、0 错误；安装注册指向新版。
- 9 项前端测试通过，包含用户目录中的实际 476 构件 GLB/JSON 数据。
- 前端 TypeScript 检查和生产构建通过。
- 浏览器实际验证双文件一起选择、分两次选择、文件夹加载；真实属性中楼板 Element ID 为 176804、标高为 Level 1。
- 实际加载旧导出文件无 Vue 警告或错误；旧几何错位仍保留，这不是新版导出效果截图。
- 尚未在运行中的 Revit 里用新版重新导出。必须人工复核门窗与墙洞位置，特别是旋转、镜像及嵌套族。
- 上次检查报告中逐面拆分、材质透明度/纹理等问题不在本次两个主要修复的验证结论内。
