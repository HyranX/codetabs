# 发布到 GitHub

当前版本为 `0.16.8-preview`。公开仓库为 https://github.com/HyranX/codetabs 。以下首次推送示例中的 `OWNER/REPO` 用于新建仓库；现有仓库无需重复添加 origin。

## 首次推送

先创建一个空的 GitHub 仓库，不让 GitHub 生成 README 或许可证。源码已包含 Apache-2.0 的 LICENSE 和 NOTICE。

```powershell
git remote add origin https://github.com/OWNER/REPO.git
git push -u origin main
```

推送前检查 `git log --format=fuller` 中的作者邮箱，以及 `git ls-files` 的文件清单。`config/`、本地归档和二进制不应在其中。

## 生成 Release 下载附件

在本机有交互桌面的环境先运行完整打包验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

下载附件是 `releases/CodeTabs-0.16.8-preview-windows-x64-portable.zip` 和同名 `.sha256`。历史包不会自动覆盖。源码打包与便携 EXE 下载是两回事。

### 方式一：使用已验证的本机便携包

在 GitHub 的 Releases 页面创建发布，标签填写 `v0.16.8-preview`，标记为预发布；将 ZIP 与 `.sha256` 拖入附件区，发布说明采用 `docs/release-notes.txt`。

也可以安装并登录 GitHub CLI 后执行：

```powershell
git tag -a v0.16.8-preview -m "CodeTabs 0.16.8-preview"
git push origin v0.16.8-preview
```

注意：仓库已包含标签触发的自动发布流程。若希望手工上传本机包，先在仓库 Actions 中禁用自动发布流程，避免同一标签同时创建两个发布。

```powershell
gh release create v0.16.8-preview ./releases/CodeTabs-0.16.8-preview-windows-x64-portable.zip ./releases/CodeTabs-0.16.8-preview-windows-x64-portable.zip.sha256 --verify-tag --prerelease --title "CodeTabs 0.16.8-preview" --notes-file docs/release-notes.txt
```

### 方式二：自动发布

保持 `.github/workflows/release.yml` 启用，推送版本标签即可。工作流会核对标签与程序集版本，编译、执行非 UI 测试、打包并创建带 ZIP/校验文件的预发布。使用仓库自动提供的 `GITHUB_TOKEN`，不用在源码中放个人令牌。

GitHub 托管运行器没有可靠的交互桌面，工作流明确使用 `-SkipUiTests`，不会宣称已执行桌面/多屏回归。本机完整测试仍然必要。CI 重新编译的 EXE/ZIP 哈希可能与本机包不同，附件须配套使用同次构建生成的校验文件。

## 新版本

同步更新 `src/AssemblyInfo.cs`、`src/app.manifest`、README、CHANGELOG 和分发说明。打包默认读取程序集版本，不需要修改脚本中的版本常量。提交后再创建相应标签。

参考：[GitHub Release 管理](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)、[gh release create](https://cli.github.com/manual/gh_release_create)。
