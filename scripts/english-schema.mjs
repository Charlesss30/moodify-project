export const collections={TaiKhoan:'accounts',NoiDungGiaiTri:'contents',Phim:'movies',Nhac:'music',TamTrang:'moods',TheLoai:'genres',DanhGia:'ratings',LichSuTamTrang:'moodHistory',_Counters:'counters'};
export const fields={TenDangNhap:'username',Email:'email',MatKhau:'passwordHash',VaiTro:'role',TrangThai:'isActive',EmailNormalized:'emailNormalized',TenDangNhapNormalized:'usernameNormalized',TieuDe:'title',LoaiNoiDung:'contentType',HinhAnh:'imageUrl',MoTa:'description',Deleted:'deleted',TheLoaiIDs:'genreIds',TheLoaiID:'genreId',DiemDanhGiaTB:'averageRating',IMDBID:'imdbId',TMDBID:'tmdbId',TenNgheSi:'artist',TenTamTrang:'name',TenTamTrangNormalized:'nameNormalized',TenTheLoai:'name',TenTheLoaiNormalized:'nameNormalized',TaiKhoanID:'accountId',NoiDungID:'contentId',TamTrangID:'moodId',SoSao:'stars',NhanXet:'comment',ThoiGian:'createdAt',VanBanDauVao:'inputText'};
for(const name of ['Album','Duration','Energy','Valence','Arousal','Danceability','Acousticness','Instrumentalness','Speechiness','Tempo','Popularity','Source','ExternalId','SourceUrl','ReleaseDate','LastSyncedAt','MoodSource','MinValence','MaxValence','MinArousal','MaxArousal','MetadataSource','NeedsReview','LegacyAudioFeatures','Value'])fields[name]=name[0].toLowerCase()+name.slice(1);
export const roles={QuanTriVien:'Admin',NguoiDung:'User',KySuAI:'AIEngineer'};
export function schemaEnglish(value){
 if(Array.isArray(value))return [...new Set(value.map(schemaEnglish))];
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([k,v])=>[collections[k]||fields[k]||k,schemaEnglish(v)]));
 if(typeof value==='string')return fields[value]||roles[value]||(value.startsWith('$')&&fields[value.slice(1)]?'$'+fields[value.slice(1)]:value);
 return value;
}
