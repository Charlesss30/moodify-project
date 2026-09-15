export const API_BASE_URL = '/api'
export const API_SERVER_URL = 'http://localhost:5170/api'

const storageKeys = { genres: 'moodify_genres', movies: 'moodify_movies', music: 'moodify_music' }

export class ApiError extends Error {
  constructor(message, status) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request(path, options = {}) {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json', ...options.headers },
    ...options,
  })
  if (!response.ok) {
    let message = `Yêu cầu API thất bại (${response.status}).`
    try {
      const body = await response.json()
      message = body.message || body.title || message
    } catch {
      // Keep the HTTP status message when the server has no JSON body.
    }
    throw new ApiError(message, response.status)
  }
  return response.status === 204 ? undefined : response.json()
}

function readMock(resource, fallback) {
  const stored = localStorage.getItem(storageKeys[resource])
  return stored ? JSON.parse(stored) : fallback
}

function writeMock(resource, data) {
  localStorage.setItem(storageKeys[resource], JSON.stringify(data))
}

export async function getResource(resource, endpoint, fallback) {
  try { return await request(endpoint) } catch { return readMock(resource, fallback) }
}

export async function getCollection(resource, endpoints, fallback) {
  let lastError
  for (const endpoint of endpoints) {
    try { return await request(endpoint) } catch (error) { lastError = error }
  }
  if (lastError?.name === 'ApiError') throw lastError
  return readMock(resource, fallback)
}

export async function saveResource(resource, endpoint, item, editingId) {
  try {
    return editingId
      ? await request(`${endpoint}/${editingId}`, { method: 'PUT', body: JSON.stringify(item) })
      : await request(endpoint, { method: 'POST', body: JSON.stringify(item) })
  } catch (error) {
    if (error?.name === 'ApiError') throw error
    const items = readMock(resource, [])
    const next = editingId ? items.map((entry) => String(entry.id) === String(editingId) ? item : entry) : [...items, item]
    writeMock(resource, next)
    return item
  }
}

export async function deleteResource(resource, endpoint, id) {
  try { await request(`${endpoint}/${id}`, { method: 'DELETE' }) } catch (error) {
    if (error?.name === 'ApiError') throw error
    const items = readMock(resource, [])
    writeMock(resource, items.filter((entry) => String(entry.id) !== String(id)))
  }
}
