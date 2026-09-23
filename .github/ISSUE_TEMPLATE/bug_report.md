name: Bug Report

about: 报告 Bug，帮助改进项目
title: '[Bug] '
labels: 'bug'
assignees: ''

body:
  - type: markdown
    attributes:
      value: |
        ## 请填写以下信息，帮助我们快速定位问题

  - type: input
    id: version
    attributes:
      label: 版本
      description: 你使用的是哪个版本？（如 v1.8、v1.7 或未发布版本）
      placeholder: v1.8
    validations:
      required: true

  - type: dropdown
    id: environment
    attributes:
      label: 运行环境
      description: 你在什么环境下复现了该问题？
      multiple: true
      options:
        - Docker 部署
        - Windows 本地运行
        - Linux 本地运行
        - macOS 本地运行
    validations:
      required: true

  - type: input
    id: browser
    attributes:
      label: 浏览器（如为前端问题）
      description: 前端问题请提供浏览器类型及版本
      placeholder: Chrome 120 / Edge 120 / Firefox 121

  - type: textarea
    id: description
    attributes:
      label: 问题描述
      description: 简明扼要地描述你遇到的问题
      placeholder: 预期行为是什么，实际遇到了什么问题
    validations:
      required: true

  - type: textarea
    id: reproduce
    attributes:
      label: 复现步骤
      description: 请逐步说明如何复现该问题
      value: |
        1. 进入「XXX」页面
        2. 点击「XXX」按钮
        3. 在弹窗中输入「XXX」
        4. 观察到 XXX 现象
    validations:
      required: true

  - type: textarea
    id: expectation
    attributes:
      label: 期望行为
      description: 你期望发生什么？
    validations:
      required: true

  - type: textarea
    id: actual
    attributes:
      label: 实际行为
      description: 实际发生了什么？（可包含错误信息、异常堆栈）
    validations:
      required: true

  - type: textarea
    id: screenshots
    attributes:
      label: 截图/录屏
      description: 如有，请提供截图或录屏以辅助说明
      placeholder: 拖拽图片或粘贴链接

  - type: textarea
    id: logs
    attributes:
      label: 日志信息
      description: 如有后端日志或浏览器控制台错误，请粘贴在此
      render: text

  - type: textarea
    id: extra
    attributes:
      label: 补充信息
      description: 任何其他相关上下文或备注
