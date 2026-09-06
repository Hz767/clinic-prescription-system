# Git 工作流

> 本文档定义了项目的Git分支策略、提交规范和协作流程。

## 目录

- [分支策略](#分支策略)
- [提交规范](#提交规范)
- [工作流程](#工作流程)
- [代码合并](#代码合并)
- [版本标签](#版本标签)
- [常用命令](#常用命令)
- [常见问题](#常见问题)

---

## 分支策略

### 分支模型

采用 **GitHub Flow** 简化模型：

```
main ──────────────────────────────────────►  (始终可发布)
         \
          feat/xxx ──► 开发完成 ──► PR ──► 合并
         \
          fix/xxx ──► 修复完成 ──► PR ──► 合并
```

### 分支类型

| 分支 | 用途 | 命名规范 | 从哪创建 | 合并到 | 生命周期 |
|------|------|---------|---------|--------|---------|
| `main` | 主分支，生产代码 | `main` | - | - | 永久 |
| `feat/*` | 新功能 | `feat/简短描述` | main | main | 合并后删除 |
| `fix/*` | Bug修复 | `fix/简短描述` | main | main | 合并后删除 |
| `hotfix/*` | 紧急修复 | `hotfix/简短描述` | main | main | 合并后删除 |
| `refactor/*` | 重构 | `refactor/简短描述` | main | main | 合并后删除 |
| `docs/*` | 文档 | `docs/简短描述` | main | main | 合并后删除 |
| `chore/*` | 杂项 | `chore/简短描述` | main | main | 合并后删除 |

### 分支命名规范

- 使用小写英文+连字符
- 简短描述功能，不超过5个单词
- 可关联Issue编号

```
# ✅ 好
feat/ai-medical-scribe
fix/prescription-amount-calc
refactor/prescription-viewmodel
docs/development-workflow
hotfix/login-crash

# ❌ 不好
feat/newFeature        // 驼峰命名
fix/bug1               // 描述不清
my-branch              // 无类型前缀
feat/add-a-new-feature-for-prescription-management-with-ai // 太长
```

### main分支保护

- main分支 **不允许直接推送**，必须通过PR合并
- PR必须通过CI检查（构建+测试）
- PR至少需要1人审查批准
- 合并后自动删除功能分支

---

## 提交规范

采用 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/v1.0.0/) 规范。

### 提交格式

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Type 类型

| 类型 | 说明 | 示例 |
|------|------|------|
| `feat` | 新功能 | `feat: 添加AI辅助写病历功能` |
| `fix` | Bug修复 | `fix: 修复处方金额计算错误` |
| `docs` | 文档变更 | `docs: 更新开发流程文档` |
| `style` | 代码格式（不影响功能） | `style: 调整缩进和空行` |
| `refactor` | 重构 | `refactor: 拆分PrescriptionViewModel` |
| `perf` | 性能优化 | `perf: 优化药品搜索速度` |
| `test` | 测试相关 | `test: 添加金额计算单元测试` |
| `build` | 构建系统/依赖 | `build: 升级EF Core到9.0` |
| `ci` | CI配置 | `ci: 添加GitHub Actions工作流` |
| `chore` | 杂项 | `chore: 更新.gitignore` |

### Scope（可选）

影响范围，使用小写：

- `prescription` - 处方相关
- `billing` - 收费相关
- `patient` - 患者相关
- `inventory` - 库存相关
- `ui` - 界面相关
- `database` - 数据库相关
- `ai` - AI功能相关
- `auth` - 登录权限相关

### Subject（主题）

- 简洁描述变更，不超过50个字符
- 使用祈使句（"添加"而不是"添加了"）
- 末尾不加句号

```
# ✅ 好
feat(prescription): 添加诊疗费字段
fix(ui): 修复性别下拉框无法选择

# ❌ 不好
feat: 添加了一个新的功能用于处理诊疗费的计算和显示 // 太长
fix: 修复bug // 描述不清
feat(prescription): 诊疗费。 // 有句号
```

### Body（正文，可选）

- 详细说明变更的原因和内容
- 每行不超过72字符
- 与主题之间空一行

```
feat(prescription): 添加诊疗费字段并修复金额计算

- Prescription实体添加ConsultationFee字段
- PrescriptionItem实体添加PackQuantity字段
- 后端金额计算改为按整盒计价
- 保存处方时重新计算明细金额，不信任前端传入值

修复 #123
```

### Footer（页脚，可选）

- 关联Issue：`修复 #123`、`关联 #456`
- 破坏性变更：`BREAKING CHANGE: 描述`

### 提交示例

#### 简单提交

```
fix(ui): 修复登录页面注册链接被遮挡
```

#### 完整提交

```
feat(ai): 集成本地llama.cpp推理引擎

- 添加LlamaCppLlmService实现ILlmService接口
- 支持通过appsettings.json配置模型路径和端口
- 添加系统自检功能，检测内存是否足够加载模型
- AI功能可选，无模型时使用NoOpLlmService不影响核心功能

关联 #45
```

#### 破坏性变更

```
refactor(database): 重构处方明细金额计算

BREAKING CHANGE: PrescriptionItem添加PackQuantity字段，
旧数据库需要运行迁移脚本。迁移脚本见docs/migrations/v1.2.0.sql
```

---

## 工作流程

### 标准开发流程

```
1. 同步main
   git checkout main
   git pull origin main

2. 创建分支
   git checkout -b feat/your-feature

3. 开发 + 小步提交
   git add .
   git commit -m "feat: 描述"

4. 同步main（每天至少一次）
   git fetch origin
   git merge origin/main

5. 推送分支
   git push origin feat/your-feature

6. 创建Pull Request
   - 填写PR标题和描述
   - 关联Issue
   - 请求审查

7. 代码审查
   - 根据审查意见修改
   - 推送修改

8. 合并
   - CI通过 + 审查批准 → Squash合并
   - 删除功能分支
```

### 详细步骤

#### 1. 开始新功能

```bash
# 确保本地main是最新的
git checkout main
git pull origin main

# 创建功能分支
git checkout -b feat/ai-medical-scribe
```

#### 2. 开发中提交

```bash
# 查看变更
git status
git diff

# 暂存并提交
git add src/Clinic.Application/Services/
git commit -m "feat(ai): 添加AI问诊服务接口"

# 继续开发...
git add .
git commit -m "feat(ai): 集成本地ASR语音识别"
```

#### 3. 同步main

```bash
# 获取远程更新
git fetch origin

# 合并main到当前分支
git merge origin/main

# 解决冲突（如有）
# 编辑冲突文件
git add <冲突文件>
git commit
```

#### 4. 推送并创建PR

```bash
git push origin feat/ai-medical-scribe

# 在GitHub界面创建Pull Request
# 或使用GitHub CLI
gh pr create --title "feat: AI智能问诊功能" --body "描述..."
```

#### 5. 审查修改

```bash
# 根据审查意见修改代码
git add .
git commit -m "fix: 根据审查意见修改"

git push origin feat/ai-medical-scribe
```

#### 6. 合并后清理

```bash
git checkout main
git pull origin main
git branch -d feat/ai-medical-scribe
```

---

## 代码合并

### 合并策略：Squash Merge

功能分支合并到main时使用 **Squash Merge**：

- 将功能分支的多个提交压缩为一个提交
- 保持main分支历史整洁
- 压缩后的提交信息遵循提交规范

```
功能分支：
  commit A: feat: 添加接口
  commit B: feat: 实现服务
  commit C: fix: 修复bug
  commit D: test: 添加测试
      ↓
Squash合并到main：
  commit: feat(ai): AI智能问诊功能（包含A+B+C+D的变更）
```

### 合并前检查

- [ ] CI构建通过
- [ ] 所有测试通过
- [ ] 代码审查批准
- [ ] 无冲突（已合并最新main）
- [ ] 文档已更新
- [ ] 数据库迁移已准备

### 禁止的操作

- ❌ 直接推送到main
- ❌ 使用 `git push --force` 到main
- ❌ 合并未通过CI的PR
- ❌ 合并自己审查的PR
- ❌ 保留已合并的功能分支

---

## 版本标签

### 标签命名

```
v{主版本}.{次版本}.{修订号}

示例：
v1.0.0
v1.1.0
v1.1.1
```

### 创建标签

```bash
# 轻量标签
git tag v1.2.0

# 附注标签（推荐）
git tag -a v1.2.0 -m "Release v1.2.0: AI智能问诊功能"

# 推送标签
git push origin v1.2.0

# 推送所有标签
git push origin --tags
```

### 查看标签

```bash
# 列出所有标签
git tag

# 查看标签详情
git show v1.2.0
```

---

## 常用命令

### 基础操作

```bash
# 查看状态
git status

# 查看变更
git diff
git diff --staged

# 查看历史
git log --oneline --graph -10

# 暂存
git add .
git add <file>

# 提交
git commit -m "message"
git commit --amend  # 修改上一次提交

# 撤销
git restore <file>          # 撤销工作区修改
git restore --staged <file> # 撤销暂存
git reset HEAD~1            # 撤销上一次提交（保留修改）
```

### 分支操作

```bash
# 创建并切换
git checkout -b <branch>

# 切换分支
git checkout <branch>

# 列出分支
git branch -a

# 删除分支
git branch -d <branch>      # 本地
git push origin --delete <branch>  # 远程

# 重命名分支
git branch -m <old> <new>
```

### 同步与合并

```bash
# 获取远程更新
git fetch origin

# 拉取并合并
git pull origin main

# 合并分支
git merge <branch>

# 变基（谨慎使用）
git rebase main
```

### 临时保存

```bash
# 保存当前修改
git stash

# 查看保存列表
git stash list

# 恢复保存
git stash pop
git stash apply stash@{0}
```

---

## 常见问题

### Q: 提交后发现写错了信息怎么办？

```bash
# 修改上一次提交信息
git commit --amend -m "新的提交信息"

# 如果已经推送，需要强制推送（仅功能分支）
git push --force-with-lease origin <branch>
```

### Q: 不小心提交了敏感信息怎么办？

1. 立即修改并提交新的版本
2. 使用 `git filter-repo` 清除历史（如果已推送到远程）
3. 轮换泄露的密钥/密码
4. **注意**：如果已推送到公开仓库，假设信息已泄露

### Q: 功能分支开发太久，main已经更新很多怎么办？

```bash
# 定期合并main到功能分支
git fetch origin
git merge origin/main

# 解决冲突后继续开发
```

### Q: 如何撤销已经推送的提交？

```bash
# 使用revert创建反向提交（安全，不修改历史）
git revert <commit-hash>
git push origin main

# 不要使用reset --hard强制推送main
```

### Q: 多个功能并行开发，如何管理？

- 每个功能一个独立分支
- 功能之间尽量解耦
- 共享的基础修改先合并到main
- 定期同步main避免冲突累积

### Q: .gitignore不生效怎么办？

```bash
# 文件已被跟踪，需要先取消跟踪
git rm --cached <file>
git commit -m "chore: 从版本控制中移除<file>"
```

---

## 附录：.gitignore说明

项目已配置的忽略规则：

| 类型 | 规则 | 说明 |
|------|------|------|
| 构建产物 | `**/bin/`, `**/obj/` | .NET编译输出 |
| 用户文件 | `*.user`, `*.suo` | IDE用户配置 |
| IDE | `.vs/`, `.idea/` | Visual Studio/Rider |
| 数据库 | `*.db`, `*.db-shm` | SQLite数据库 |
| 日志 | `*.log` | 运行日志 |
| 备份 | `Backups/`, `*.bak` | 备份文件 |
| 临时 | `*.tmp`, `*~` | 临时文件 |
| NuGet | `*.nupkg` | NuGet包 |

**注意**：数据库文件不纳入版本控制，测试数据通过DbSeeder初始化。
