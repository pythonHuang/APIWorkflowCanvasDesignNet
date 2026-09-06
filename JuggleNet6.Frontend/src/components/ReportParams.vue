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
      <!-- 下拉（可多选/模糊查询） -->
      <el-select v-else-if="p.type==='select'" v-model="values[p.name]" size="small" style="width:170px" clearable
        :multiple="!!p.multiple" :filterable="!!p.filterable" :collapse-tags="!!p.multiple" :placeholder="p.multiple ? '可多选' : '请选择'">
        <el-option v-for="(o, i) in optionsOf(p)" :key="i" :label="optionLabel(p, o)" :value="optionValue(p, o)" />
      </el-select>
      <!-- 单选组 -->
      <el-radio-group v-else-if="p.type==='radio'" v-model="values[p.name]" size="small">
        <el-radio v-for="(o, i) in optionsOf(p)" :key="i" :value="optionValue(p, o)">{{ optionLabel(p, o) }}</el-radio>
      </el-radio-group>
      <!-- 筛选组（多选） -->
      <el-checkbox-group v-else-if="p.type==='checkgroup'" v-model="values[p.name]" size="small">
        <el-checkbox v-for="(o, i) in optionsOf(p)" :key="i" :value="optionValue(p, o)">{{ optionLabel(p, o) }}</el-checkbox>
      </el-checkbox-group>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import request from '../utils/request'
import { parseOptions } from '../utils/reportParams'

const props = defineProps<{
  params: Array<any>
  values: Record<string, any>
  /** 报表 ID（选项绑定数据集时用） */
  reportId?: number | string
}>()

// 数据集选项缓存：paramName → [{value,label}] 或原始行数组
const datasetRows = ref<Record<string, any[]>>({})
const optionsLoaded = ref<Record<string, boolean>>({})

onMounted(() => {
  for (const p of props.params || []) {
    if (p.optionsSource === 'dataset' && p.optionsDataset) loadDatasetOptions(p)
  }
})

async function loadDatasetOptions(p: any) {
  if (!props.reportId) return
  const key = `${p.optionsDataset}|${p.optionsValueField}|${p.optionsLabelField}`
  if (datasetRows.value[key]) { optionsLoaded.value[p.name] = true; return }
  try {
    const res: any = await request.post('/report/dataset-options', { id: props.reportId, datasetId: p.optionsDataset })
    datasetRows.value[key] = res.data || []
  } catch { datasetRows.value[key] = [] }
  optionsLoaded.value[p.name] = true
}

/** 选项列表：固定选项或数据集绑定 */
function optionsOf(p: any): any[] {
  if (p.optionsSource === 'dataset') {
    const key = `${p.optionsDataset}|${p.optionsValueField}|${p.optionsLabelField}`
    return datasetRows.value[key] || []
  }
  return parseOptions(p)
}

function optionValue(p: any, o: any): string {
  if (typeof o === 'object') return String(o[p.optionsValueField || 'value'] ?? '')
  return String(o)
}

function optionLabel(p: any, o: any): string {
  if (typeof o === 'object') return String(o[p.optionsLabelField || 'label'] ?? o[p.optionsValueField || 'value'] ?? '')
  return String(o)
}
</script>
