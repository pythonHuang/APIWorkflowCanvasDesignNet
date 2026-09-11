<template>
  <el-container style="height:100vh">
    <!-- 侧边栏 -->
    <el-aside width="220px" style="background:#001529;overflow:hidden;">
      <div class="sidebar-logo">
        <span>⚡</span> Juggle
      </div>
      <el-scrollbar style="height:calc(100vh - 60px);overflow-y:auto;">
        <el-menu :default-active="activeMenu" router background-color="#001529" 
          text-color="#aaa" active-text-color="#fff" style="border:none;">
          <el-sub-menu index="monitor" v-if="hasMenu('/flow/dashboard')">
            <template #title>
              <el-icon><Histogram /></el-icon>
              <span>监控</span>
            </template>
            <el-menu-item index="/flow/dashboard">仪表盘</el-menu-item>
            <el-menu-item index="/monitor/topology">API拓扑图</el-menu-item>
            <el-menu-item index="/monitor/alert-rule">告警规则</el-menu-item>
            <el-menu-item index="/monitor/alert-record">告警记录</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="flow" v-if="hasMenu('/flow/define')">
            <template #title>
              <el-icon><Connection /></el-icon>
              <span>流程管理</span>
            </template>
            <el-menu-item index="/flow/define" v-if="hasMenu('/flow/define')">流程定义</el-menu-item>
            <el-menu-item index="/flow/list" v-if="hasMenu('/flow/list')">流程列表</el-menu-item>
            <el-menu-item index="/flow/log" v-if="hasMenu('/flow/log')">执行日志</el-menu-item>
            <el-menu-item index="/flow/testcase" v-if="hasMenu('/flow/testcase')">测试用例</el-menu-item>
            <el-menu-item index="/flow/async-result" v-if="hasMenu('/flow/async-result')">异步结果查询</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="suite" v-if="hasMenu('/suite/list')">
            <template #title>
              <el-icon><Grid /></el-icon>
              <span>套件管理</span>
            </template>
            <el-menu-item index="/suite/list">套件列表</el-menu-item>
          </el-sub-menu>
          <el-menu-item index="/object/list" v-if="hasMenu('/object/list')">
            <el-icon><DataBoard /></el-icon>
            <span>对象管理</span>
          </el-menu-item>
          <el-sub-menu index="report" v-if="hasMenu('/report/dataview')">
            <template #title>
              <el-icon><Document /></el-icon>
              <span>报表</span>
            </template>
            <el-menu-item index="/report/dataview">数据视图</el-menu-item>
            <el-menu-item index="/report/design">报表管理</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="report-view" v-if="reportMenuList.length > 0">
            <template #title>
              <el-icon><Document /></el-icon>
              <span>报表查询</span>
            </template>
            <el-menu-item v-for="r in reportMenuList" :key="r.id" :index="`/report/view/${r.id}`">{{ r.name }}</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="kb">
            <template #title>
              <el-icon><Collection /></el-icon>
              <span>知识库</span>
            </template>
            <el-menu-item index="/kb/list">知识库管理</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="ai">
            <template #title>
              <el-icon><MagicStick /></el-icon>
              <span>模型助手</span>
            </template>
            <el-menu-item index="/ai/assistants">模型助手管理</el-menu-item>
            <el-menu-item index="/ai/flow-assistant">流程智能编排助手</el-menu-item>
            <el-menu-item index="/ai/api-assistant">接口智能接入助手</el-menu-item>
            <el-menu-item index="/ai/report-assistant">报表智能助手</el-menu-item>
            <el-menu-item v-for="a in assistantMenuList" :key="a.id" :index="`/ai/assistant/${a.id}`">
            <img v-if="isImageIcon(a.icon)" :src="a.icon" style="width:16px;height:16px;margin-right:6px;vertical-align:-3px" />
            <span v-else-if="a.icon" style="margin-right:6px">{{ a.icon }}</span>
            {{ a.assistantName }}
          </el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="system" v-if="hasMenu('/system/token')">
            <template #title>
              <el-icon><Setting /></el-icon>
              <span>系统设置</span>
            </template>
            <el-menu-item index="/system/token" v-if="hasMenu('/system/token')">Token管理</el-menu-item>
            <el-menu-item index="/system/datasource" v-if="hasMenu('/system/datasource')">数据源管理</el-menu-item>
            <el-menu-item index="/system/static-var" v-if="hasMenu('/system/static-var')">静态变量</el-menu-item>
            <el-menu-item index="/system/schedule" v-if="hasMenu('/system/schedule')">定时任务</el-menu-item>
            <el-menu-item index="/system/webhook" v-if="hasMenu('/system/webhook')">Webhook 管理</el-menu-item>
            <el-menu-item index="/system/users" v-if="hasMenu('/system/users')">用户管理</el-menu-item>
            <el-menu-item index="/system/role" v-if="hasMenu('/system/role')">角色管理</el-menu-item>
            <el-menu-item index="/system/tenant" v-if="hasMenu('/system/tenant')">租户管理</el-menu-item>
            <el-menu-item index="/system/config" v-if="hasMenu('/system/config')">系统配置</el-menu-item>
            <el-menu-item index="/system/ai-provider" v-if="hasMenu('/system/config')">大模型设置</el-menu-item>
          <el-menu-item index="/system/redis" v-if="hasMenu('/system/config')">Redis 配置</el-menu-item>
          <el-menu-item index="/system/skills" v-if="hasMenu('/system/config')">Skill 管理</el-menu-item>
            <el-menu-item index="/system/login-log" v-if="hasMenu('/system/login-log')">登录日志</el-menu-item>
            <el-menu-item index="/system/audit-log" v-if="hasMenu('/system/audit-log')">审计日志</el-menu-item>
          </el-sub-menu>
        </el-menu>
      </el-scrollbar>
    </el-aside>

    <el-container>
      <!-- 顶部导航 -->
      <el-header style="background:#fff;border-bottom:1px solid #eee;display:flex;align-items:center;justify-content:space-between;padding:0 24px">
        <el-breadcrumb separator="/">
          <el-breadcrumb-item>Juggle 接口编排平台</el-breadcrumb-item>
        </el-breadcrumb>
        <el-dropdown @command="handleCommand">
          <span style="cursor:pointer;display:flex;align-items:center;gap:8px">
            <el-avatar :size="32" style="background:#0f3460">{{ userName?.charAt(0)?.toUpperCase() }}</el-avatar>
            {{ userName }}
            <el-icon><ArrowDown /></el-icon>
          </span>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="logout">退出登录</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>

      <!-- 主内容 -->
      <el-main class="main-content">
        <div class="main-scroll">
          <router-view />
        </div>
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { Connection, Grid, DataBoard, Setting, ArrowDown, Histogram, Document, MagicStick, Collection } from '@element-plus/icons-vue'
import request from '../utils/request'

const route = useRoute()
const router = useRouter()
const userName = computed(() => localStorage.getItem('userName') || 'User')
const activeMenu = computed(() => route.path)

// 报表查询菜单：每个启用报表一个子菜单
const reportMenuList = ref<any[]>([])
// 模型助手菜单：每个启用的自定义助手一个子菜单
const assistantMenuList = ref<any[]>([])
onMounted(async () => {
  try {
    const res = await request.post('/report/page', { pageNum: 1, pageSize: 200 })
    reportMenuList.value = (res.data?.list || []).filter((r: any) => r.status === 1)
  } catch { /* 忽略菜单加载失败 */ }
  try {
    const res2 = await request.get('/ai/assistants/enabled')
    assistantMenuList.value = res2.data || []
  } catch { /* 未配置助手时忽略 */ }
})

// 权限菜单列表
const menuKeys = ref<string[]>([])
try {
  const stored = localStorage.getItem('menuKeys')
  if (stored) menuKeys.value = JSON.parse(stored)
} catch { /* ignore */ }

const roleCode = computed(() => localStorage.getItem('roleCode') || '')

function isImageIcon(icon: string | undefined | null): boolean {
  return !!icon && (icon.startsWith('data:image') || icon.startsWith('http'))
}

function hasMenu(menuKey: string): boolean {
  // 超级管理员显示所有菜单
  if (roleCode.value === 'admin') return true
  // 没有配置角色，显示所有菜单（兼容旧版本）
  if (menuKeys.value.length === 0) return true
  return menuKeys.value.includes(menuKey)
}

function handleCommand(cmd: string) {
  if (cmd === 'logout') {
    ElMessageBox.confirm('确认退出登录？', '提示', { type: 'warning' })
      .then(() => {
        localStorage.removeItem('token')
        localStorage.removeItem('userName')
        localStorage.removeItem('roleCode')
        localStorage.removeItem('menuKeys')
        router.push('/login')
      })
  }
}
</script>

<style scoped>
.sidebar-logo {
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
  font-weight: bold;
  color: #fff;
  gap: 8px;
  border-bottom: 1px solid #0a2540;
}
.main-content {
  background: #f5f7fa;
  overflow: hidden;
  padding: 0;
  display: flex;
  flex-direction: column;
}
.main-scroll {
  flex: 1;
  min-height: 0;
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
}
</style>
