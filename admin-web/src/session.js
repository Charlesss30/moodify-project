import { request, tokenKey } from './api.js'

export async function resumeAdminSession() {
  const session = await request('/Auth/admin-session', { method: 'POST' })
  if (session?.token) sessionStorage.setItem(tokenKey, session.token)
}
