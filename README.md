# CredentialManager

一个「OIDC 登录 + 自管理下游凭证」的 Web 服务。

- 用户通过 OIDC（实际对接 [Dex](https://dexidp.io/)）登录；登录后可以创建、吊销**自己的**下游凭证。
- 管理员（Dex 中属于某个指定 `groups` 的用户）可以查看并吊销**所有**用户的凭证。
- 本服务负责记录「哪个下游凭证归属哪个 OIDC 用户」的所有权信息；下游本身不管用户，只提供 `create` / `revoke` / `query` 三个 HTTP 接口。
- 数据用 LiteDB（嵌入式文档数据库）持久化。

## 技术栈

- .NET 10，Razor Pages
- [CliFx](https://github.com/Tyrrrz/CliFx) 用于命令行参数
- LiteDB（嵌入式文档数据库，用于存储凭证所有权）
- `Microsoft.AspNetCore.Authentication.OpenIdConnect`（标准 OIDC code flow）

## 项目结构

```
src/
  Tjslp.CredentialManager/          主服务（Razor Pages + CLI）
  Tjslp.CredentialManager.Protocol/ 下游交互协议类型（record，可复用）
tests/
  Tjslp.CredentialManager.E2E/      端到端测试（含 mock OIDC + mock 下游）
  Tjslp.CredentialManager.Live/     本地预览程序（复用 E2E 的 mock）
```

## 运行

```sh
dotnet run --project src/Tjslp.CredentialManager -- \
  --listen      http://127.0.0.1:8080 \
  --title       "我的平台" \
  --data        /var/lib/credentialmanager \
  --downstream  https://downstream.example.com \
  --administrator  administrators \
  --oidc        https://dex.example.com \
  --oidc-id     credential-manager \
  --oidc-secret <secret> \
  --oidc-ca     /path/to/oidc-ca.pem
```

### 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--listen` | 监听地址（完整 URL），必填 |
| `--title` | 页面标题里展示的平台名，必填 |
| `--data` | 数据目录（LiteDB 文件 `credentials.db` 会写入此目录），必填 |
| `--downstream` | 下游服务 base URL，必填 |
| `--administrator` | 管理员所属的 Dex `groups` 名（只支持一个），必填 |
| `--oidc` | OIDC authority（Dex 地址），必填 |
| `--oidc-id` | OIDC client id，必填 |
| `--oidc-secret` | OIDC client secret，必填。也可用环境变量 `CREDENTIAL_MANAGER_ARGUMENT_OIDC_SECRET` 提供 |
| `--oidc-ca` | OIDC CA 证书（PEM），可选；提供时用于自建信任链校验 OIDC 服务端证书 |

> 敏感值（如 client secret）建议通过环境变量 `CREDENTIAL_MANAGER_ARGUMENT_OIDC_SECRET` 传入，避免出现在命令行历史 / 进程列表中。

## 下游协议

主服务通过 HTTP JSON（`POST`）调用下游。所有请求、响应均为 JSON 对象。下游 base URL 加上固定路径后缀即为完整地址：`/create`、`/revoke`、`/query`。

### create

`POST {downstream}/create`

```json
{ "expire": "2026-10-01T00:00:00Z" }
```

`expire` 可为 `null`，**表示「永久」**（不指定过期时间）。

响应：

```json
{ "credentialId": "…", "credential": "…", "expire": "2026-10-01T00:00:00Z" }
```

- `credentialId`：下游返回的凭证 ID。
- `credential`：凭证本身（密钥）。**只在此处返回一次**，之后无法再次查看。
- `expire`：到期时间；`null` 表示永久。

### revoke

`POST {downstream}/revoke`

```json
{ "credentialId": "…" }
```

响应：

```json
{ "succeeded": true }
```

### query

`POST {downstream}/query`

```json
{ "credentialIds": ["…", "…"] }
```

只需返回这些 ID 对应的状态（用于按需同步，而不是拉取所有）。响应：

```json
{ "credentials": [ { "credentialId": "…", "expire": "2026-10-01T00:00:00Z" } ] }
```

`expire` 为 `null` 表示永久。

> 这些类型定义在 `src/Tjslp.CredentialManager.Protocol`，下游实现方可直接引用该程序集，避免手写协议类型。

## 行为说明

- **同步是自动的**：页面加载时，主服务会向下游 `query` 本用户（或全部）凭证 ID 的当前过期时间，并清理下游已不存在的本地记录。没有手动「同步」按钮。
- 登录状态由 cookie 维持；服务重启后用户需重新登录（Data Protection / 登录态不做持久化）。
- 删除 `wwwroot`，CSS 内嵌在共享 Razor 片段中（`Pages/_Styles.cshtml`），便于部署时无需处理静态资源路径。
- 假定部署在同机反向代理之后；已启用 `ForwardedHeaders`（`X-Forwarded-For` / `X-Forwarded-Proto`），默认信任 loopback。

## 开发

```sh
# 构建
dotnet build

# 端到端测试（含 mock OIDC + mock 下游，不需要真实 Dex / 下游）
dotnet test tests/Tjslp.CredentialManager.E2E

# 本地预览（起 mock OIDC + mock 下游 + 主服务）
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project tests/Tjslp.CredentialManager.Live -- https://127.0.0.1:8080
```

> 本地预览需要 HTTPS（http 下 correlation cookie 会被浏览器丢弃导致登录失败），因此用 `Development` 环境让 `CreateBuilder` 挂上 .NET dev 证书，浏览器需手动放行该证书。

---

## ⚠️ 说明：AI 参与的部分

本项目中的**端到端测试（`tests/` 下的 mock OIDC、mock 下游、测试套件）**以及**前端页面（Razor 页面、样式、交互）**部分由 AI 生成，**基本没有经过人工逐行检查与调整**。
