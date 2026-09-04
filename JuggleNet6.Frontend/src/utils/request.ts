import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '../router'

const request = axios.create({
  baseURL: '/api',
  timeout: 30000
})

// 请求拦截：添加 JWT token
request.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截：统一处理错误
request.interceptors.response.use(
  (response) => {
    const data = response.data
    // blob 下载（文件导出）直接放行
    if (data instanceof Blob) return response
    if (data.code === 200) {
      return data
    } else if (data.code === 401) {
      localStorage.removeItem('token')
      router.push('/login')
      ElMessage.error('登录已过期，请重新登录')
      return Promise.reject(new Error(data.message))
    } else {
      ElMessage.error(data.message || '操作失败')
      return Promise.reject(new Error(data.message))
    }
  },
  (error) => {
    // HTTP 401 状态码（JWT 无效/过期）—— 清除 token 并跳转登录
    if (error.response?.status === 401) {
      localStorage.removeItem('token')
      router.push('/login')
      ElMessage.error('登录已过期，请重新登录')
    } else {
      ElMessage.error(error.response?.data?.message || error.message || '网络错误')
    }
    return Promise.reject(error)
  }
)

export default request
