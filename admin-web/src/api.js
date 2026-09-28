export const API_BASE_URL = '/api'
export const API_SERVER_URL = 'http://localhost:5170/api'
export const tokenKey = 'moodify_admin_token'
export class ApiError extends Error {
  constructor(message, status) { super(message); this.name = 'ApiError'; this.status = status }
}
export async function request(path, options = {}) {
  const token = sessionStorage.getItem(tokenKey)
  const response = await fetch(API_BASE_URL + path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...options.headers },
  })
  if (!response.ok) {
    let message = 'API request failed (' + response.status + ').'
    try { const body = await response.json(); message = body.message || body.title || message } catch { /* no JSON */ }
    if (response.status === 401) {
      sessionStorage.removeItem(tokenKey)
      window.dispatchEvent(new Event('moodify-session-expired'))
    }
    throw new ApiError(message, response.status)
  }
  return response.status === 204 ? undefined : response.json()
}
export async function getResource(_resource, endpoint) { return request(endpoint) }
export async function getCollection(_resource, endpoints) { return request(endpoints[0]) }
export async function saveResource(_resource, endpoint, item, editingId) {
  return request(editingId != null ? endpoint + '/' + encodeURIComponent(editingId) : endpoint, {
    method: editingId != null ? 'PUT' : 'POST', body: JSON.stringify(item),
  })
}
export async function deleteResource(_resource, endpoint, id) {
  return request(endpoint + '/' + encodeURIComponent(id), { method: 'DELETE' })
}
