name: Pull Request Template

about: ''
pull_request:
  body:
    - type: markdown
      attributes:
        value: |
          ## 变更类型
          请勾选适用的类型：

    - type: checkboxes
      id: type
      attributes:
        label: 变更类型
        options:
          - label: 'feat — 新功能'
          - label: 'fix — Bug 修复'
          - label: 'refactor — 代码重构（无功能变化）'
          - label: 'docs — 文档更新'
          - label: 'style — 代码格式调整（不影响逻辑）'
          - label: 'perf — 性能优化'
          - label: 'test — 测试相关'
          - label: 'chore — 构建/CI/工具链调整'

    - type: textarea
      id: description
      attributes:
        label: 变更描述
        description: 简明扼要地描述本次变更的内容
      validations:
        required: true

    - type: textarea
      id: related-issue
      attributes:
        label: 相关 Issue
        description: 如有，请填写相关 Issue 编号（如 #123）
        placeholder: 'Fixes #'

    - type: textarea
      id: screenshots
      attributes:
        label: 截图（如适用）
        description: 前端变更请附带截图或 GIF
        placeholder: 拖拽图片或粘贴链接

    - type: textarea
      id: checklist
      attributes:
        label: 自查清单
        description: 提交前请确认以下事项
        value: |
          - [ ] 代码已通过本地构建/测试
          - [ ] 已更新相关文档（如适用）
          - [ ] 遵循了项目的编码规范
