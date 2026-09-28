import { request } from './api.js'
export async function allPages(path) {
  const items=[]
  for(let page=1;;page++){
    const data=await request(path+'?page='+page)
    items.push(...data.items)
    if(!data.items.length||items.length>=data.total)return {...data,items,total:items.length}
  }
}
export function enhanceList(host,{nodes,filterColumn,filterLabel='Filter',dateLabel='Updated',dateAvailable=true,filterTabs,selectedFilter,onFilterChange}={}) {
  if(!host)return
  const rows=nodes||[...host.querySelectorAll('tbody tr')].filter(row=>!row.querySelector('td[colspan]'))
  host.querySelectorAll('tbody td[colspan]').forEach(cell=>cell.parentElement.remove())
  const table=host.querySelector('.table-scroll')||host.querySelector('table')
  const anchor=nodes?.[0]||table
  if(!anchor)return
  host.querySelectorAll('.pagination,.table-meta,#user-search').forEach(x=>x.remove())
  const bar=document.createElement('form');bar.className='list-controls'
  bar.innerHTML='<label class="field"><span>Search this list</span><input name="search" type="search" placeholder="Enter a name or keyword"></label><div class="list-options"><button class="primary-button" type="submit">Search</button><label class="field"><span>Sort by</span><select name="sort"><option value="az">Name: A–Z</option><option value="za">Name: Z–A</option>'+(dateAvailable?'<option value="new">'+dateLabel+': newest</option><option value="old">'+dateLabel+': oldest</option>':'')+'</select></label></div>'
  const entries=rows.map((row,index)=>({row,index,name:(row.querySelector('td,summary')?.textContent||row.textContent).trim(),text:(row.cells?[...row.cells].map(cell=>cell.querySelector('select')?.value||cell.textContent).join(' '):row.querySelector('summary')?.textContent||row.textContent).toLocaleLowerCase(),time:Date.parse(row.dataset.updated||'')||0,filter:filterColumn==null?'':(row.cells?.[filterColumn]?.querySelector('select')?.value||row.cells?.[filterColumn]?.textContent||'').trim()}))
  let filter
  let activeFilter=selectedFilter||filterTabs?.[0]?.value
  let tabs
  if(filterTabs){
    tabs=document.createElement("div");tabs.className="genre-type-tabs";tabs.setAttribute("role","tablist");tabs.setAttribute("aria-label","Genre type")
    for(const option of filterTabs){const b=document.createElement("button");b.type="button";b.dataset.genreType=option.value;b.textContent=option.label;b.setAttribute("role","tab");b.onclick=()=>{activeFilter=option.value;onFilterChange?.(activeFilter);page=1;draw()};b.onkeydown=e=>{if(!["ArrowLeft","ArrowRight","Home","End"].includes(e.key))return;e.preventDefault();const buttons=[...tabs.children],index=buttons.indexOf(b);const next=e.key==="Home"?0:e.key==="End"?buttons.length-1:(index+(e.key==="ArrowRight"?1:-1)+buttons.length)%buttons.length;buttons[next].click();buttons[next].focus()};tabs.append(b)}
    bar.querySelector(".list-options").append(tabs)
  }
  if(filterColumn!=null&&!filterTabs){const label=document.createElement('label');label.className='field';const span=document.createElement('span');span.textContent=filterLabel;filter=document.createElement('select');filter.name='filter';filter.add(new Option('All',''));[...new Set(entries.flatMap(e=>e.filter.split(',').map(x=>x.trim())).filter(Boolean))].sort().forEach(x=>filter.add(new Option(x,x)));label.append(span,filter);bar.querySelector('.list-options').append(label)}
  anchor.before(bar)
  const pager=document.createElement('div');pager.className='pagination list-pagination';pager.innerHTML='<button type="button" data-prev>Previous</button><span aria-live="polite"></span><button type="button" data-next>Next</button>'
  const empty=document.createElement('p');empty.className='list-empty';empty.textContent='No results found.'
  const parent=rows[0]?.parentElement
  ;(table||rows.at(-1)).after(empty,pager)
  let page=1
  function draw(){
    const query=bar.elements.search.value.trim().toLocaleLowerCase(),sort=bar.elements.sort.value
    if(tabs) for(const b of tabs.children){const selected=b.dataset.genreType===activeFilter;b.setAttribute("aria-selected",String(selected));b.tabIndex=selected?0:-1}
    const visible=entries.filter(e=>e.text.includes(query)&&(!filterTabs||e.filter===activeFilter||e.filter==="Both")&&(!filter?.value||e.filter.split(',').map(x=>x.trim()).includes(filter.value)))
    visible.sort((a,b)=>{
      if(sort==='new'||sort==='old'){if(!a.time||!b.time)return a.time? -1:b.time?1:a.name.localeCompare(b.name);return (sort==='new'?b.time-a.time:a.time-b.time)||a.name.localeCompare(b.name)}
      return (sort==='za'?-1:1)*a.name.localeCompare(b.name,undefined,{numeric:true,sensitivity:'base'})||a.index-b.index
    })
    const pages=Math.max(1,Math.ceil(visible.length/12));page=Math.min(page,pages)
    entries.forEach(e=>e.row.hidden=true)
    visible.forEach((e,i)=>{e.row.hidden=i<(page-1)*12||i>=page*12;if(parent)parent.append(e.row)})
    // Keep controls after mood cards when the cards share the host.
    if(nodes){host.append(empty,pager)}
    empty.hidden=visible.length!==0
    pager.querySelector('span').textContent=`${visible.length?(page-1)*12+1:0}–${Math.min(page*12,visible.length)} of ${visible.length} · Page ${page}/${pages}`
    pager.querySelector('[data-prev]').disabled=page===1;pager.querySelector('[data-next]').disabled=page===pages
  }
  bar.onsubmit=e=>{e.preventDefault();page=1;draw()}
  bar.onchange=()=>{page=1;draw()}
  bar.elements.search.addEventListener('input',()=>{page=1;draw()})
  pager.querySelector('[data-prev]').onclick=()=>{page--;draw()};pager.querySelector('[data-next]').onclick=()=>{page++;draw()}
  draw()
}
