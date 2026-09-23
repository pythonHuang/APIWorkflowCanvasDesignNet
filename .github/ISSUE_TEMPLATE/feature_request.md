name: Feature Request

about: 提出新功能或改进建议
title: '[Feature] '
labels: 'enhancement'
assignees: ''

body:
  - type: markdown
    attributes:
      value: |
        ## 新功能 / 改进建议

  - type: textarea
    id: description
    attributes:
      label: 功能描述
      description: 描述你想要的新功能或改进，尽量具体
      placeholder: 我希望平台能够支持 XXX...
    validations:
      required: true

  - type: textarea
    id: motivation
    attributes:
      label: 使用动机
      description: 为什么需要这个功能？解决了什么痛点？
      placeholder: 目前平台缺少 XXX，导致用户无法...
    validations:
      required: true

  - type: textarea
    id: implementation
    attributes:
      label: 实现思路（可选）
      description: 如果你有初步的实现方案或思路，请在此分享
      placeholder: 建议在 XXX 模块增加 YYY 功能...

  - type: textarea
    id: alternatives
    attributes:
      label: 备选方案
      description: 你是否考虑过其他实现方式？它们各自的优缺点是什么？

  - type: textarea
    id: screenshots
    attributes:
      label: 参考截图/设计稿（可选）
      description: 如有参考实现或设计稿，请在此附上
      placeholder: 拖拽图片或粘贴链接

  - type: textarea
    id: extra
    attributes:
      label: 补充信息
      description: 任何其他相关上下文或备注
