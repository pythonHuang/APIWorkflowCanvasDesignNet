<template>
  <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">
    <div v-for="p in params" :key="p.name" style="display:flex;flex-direction:column;gap:2px">
      <span style="font-size:12px;color:#666">{{ p.label || p.name }}</span>
      <!-- 文本 -->
      <el-input v-if="!p.type || p.type==='text'" v-model="values[p.name]" size="small" style="width:160px" clearable placeholder="请输入" />
      <!-- 数字 -->
      <el-input-number v-else-if="p.type==='number' || p.type==='int'" v-model="values[p.name]" size="small" style="width:160px" :controls="false" placeholder="请输入数字" />
      <!-- 日期 -->
      <el-date-picker v-else-if="p.type==='date'" v-model="values[p.name]" size="small" type="date" value-format="YYYY-MM-DD" style="width:160px" placeholder="选择日期" />
      <!-- 开关 -->
      <el-switch v-else-if="p.type==='switch'" v-model="values[p.name]" size="small" />
      <!-- 下拉 -->
      <el-select v-else-if="p.type==='select'" v-model="values[p.name]" size="small" style="width:160px" clearable placeholder="请选择">
        <el-option v-for="(o, i) in selectOptions(p)" :key="i" :label="o" :value="o" />
      </el-select>
    </div>
  </div>
</template>

<script setup lang="ts">
const props = defineProps<{
  params: Array<{ name: string; label?: string; type?: string; options?: string; default?: any }>
  values: Record<string, any>
}>()

/** 下拉选项解析：支持逗号或换行分隔 */
function selectOptions(p: any): string[] {
  if (Array.isArray(p.options)) return p.options
  return String(p.options || '').split(/[,，\n]/).map((s: string) => s.trim()).filter(Boolean)
}
</script>
