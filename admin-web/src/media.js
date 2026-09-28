export const placeholderImage = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="120" height="160" viewBox="0 0 120 160"><rect width="120" height="160" fill="#252a36"/><path d="M30 100V60h60v40z M35 94l16-20 13 14 10-11 12 17" stroke="#81899b" stroke-width="3" fill="none"/><circle cx="77" cy="67" r="5" fill="#81899b"/></svg>')
export function fillMissingImages(host) {
  host?.querySelectorAll('img').forEach(img => {
    img.onerror = () => { img.onerror = null; img.src = placeholderImage; img.alt = 'No image available' }
    if (!img.getAttribute('src')) { img.src = placeholderImage; img.alt = 'No image available' }
  })
}
