import { resumeAdminSession } from '../admin-web/src/session.js'
import test from 'node:test'
import assert from 'node:assert/strict'
import { request, saveResource, getCollection, deleteResource, tokenKey } from '../admin-web/src/api.js'
const entries = new Map()
globalThis.sessionStorage = {getItem:k=>entries.get(k),setItem:(k,v)=>entries.set(k,v),removeItem:k=>entries.delete(k)}
globalThis.window = new EventTarget()
test('API keeps credentials, errors and server IDs consistent', async () => {
  entries.set(tokenKey,'test-token')
  let captured
  globalThis.fetch=async(url,options)=>{captured={url,options};return Response.json({theLoaiID:23,tenTheLoai:'Drama'},{status:201})}
  const result=await saveResource('genres','/Genres',{TenTheLoai:'Drama'})
  assert.equal(result.theLoaiID,23)
  assert.equal(captured.options.headers.Authorization,'Bearer test-token')
  assert.deepEqual(JSON.parse(captured.options.body),{TenTheLoai:'Drama'})
  await request('/Genres',{headers:{'X-Test':'yes'}})
  assert.equal(captured.options.headers.Authorization,'Bearer test-token')
  assert.equal(captured.options.headers['X-Test'],'yes')
  await deleteResource('movies','/Movies','a/b')
  assert.equal(captured.url,'/api/Movies/a%2Fb')
  globalThis.fetch=async()=>{throw new TypeError('Network disconnected')}
  await assert.rejects(()=>getCollection('genres',['/Genres'],[{id:'fake'}]),/Network disconnected/)
  await assert.rejects(()=>saveResource('genres','/Genres',{}),/Network disconnected/)
  globalThis.fetch=async()=>Response.json({message:'Forbidden'},{status:403})
  await assert.rejects(()=>deleteResource('genres','/Genres',23),e=>e.status===403)
  let expired=false
  window.addEventListener('moodify-session-expired',()=>expired=true,{once:true})
  globalThis.fetch=async()=>new Response(null,{status:401})
  await assert.rejects(()=>request('/Auth/me'),e=>e.status===401)
  assert.equal(entries.has(tokenKey),false)
  assert.equal(expired,true)
})

test('dashboard redeems the browser handoff before using an older tab session', async () => {
  entries.set(tokenKey,'old-admin-session')
  globalThis.fetch=async(url,options)=>{
    assert.equal(url,'/api/Auth/admin-session')
    assert.equal(options.method,'POST')
    return Response.json({token:'new-admin-session'})
  }
  await resumeAdminSession()
  assert.equal(entries.get(tokenKey),'new-admin-session')
  globalThis.fetch=async()=>new Response(null,{status:204})
  await resumeAdminSession()
  assert.equal(entries.get(tokenKey),'new-admin-session')
  globalThis.fetch=async()=>new Response(null,{status:401})
  await assert.rejects(()=>resumeAdminSession(),e=>e.status===401)
  assert.equal(entries.has(tokenKey),false)
})
