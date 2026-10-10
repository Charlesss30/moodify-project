import fs from 'node:fs';
import path from 'node:path';
const out='docs/.mobile-report-word';fs.mkdirSync(path.join(out,'word'),{recursive:true});fs.mkdirSync(path.join(out,'_rels'),{recursive:true});fs.mkdirSync(path.join(out,'word/_rels'),{recursive:true});
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
const p=(text,style='Normal')=>`<w:p><w:pPr><w:pStyle w:val="${style}"/></w:pPr><w:r><w:t xml:space="preserve">${esc(text)}</w:t></w:r></w:p>`;
const bullet=text=>p('• '+text);
const heading=text=>p(text,'Heading2');
const page=(title,body)=>p(title,'Heading1')+body;
const br='<w:p><w:r><w:br w:type="page"/></w:r></w:p>';
const table=(headers,rows)=>`<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/><w:tblBorders>${['top','left','bottom','right','insideH','insideV'].map(x=>`<w:${x} w:val="single" w:sz="4" w:color="D5DEE8"/>`).join('')}</w:tblBorders></w:tblPr>${[headers,...rows].map((row,i)=>'<w:tr>'+(i===0?'<w:trPr><w:tblHeader/></w:trPr>':'')+row.map(text=>'<w:tc><w:tcPr><w:tcW w:w="'+Math.floor(9300/headers.length)+'" w:type="dxa"/>'+(i===0?'<w:shd w:fill="E8EEF7"/>':'')+'</w:tcPr>'+p(text,'TableText')+'</w:tc>').join('')+'</w:tr>').join('')}</w:tbl>`;
const sections=[
  {
    "title": "BÁO CÁO THỰC HIỆN ỨNG DỤNG NGƯỜI DÙNG",
    "items": [
      [
        "subtitle",
        "Moodify – Gợi ý phim và âm nhạc theo tâm trạng"
      ],
      [
        "subtitle",
        "Phạm vi: Mobile App Flutter | Ngày tổng hợp: 03/10/2026"
      ],
      [
        "h",
        "1. NHỮNG NỘI DUNG ĐÃ THỰC HIỆN"
      ],
      [
        "h",
        "1.1. Nền tảng và luồng sử dụng"
      ],
      [
        "p",
        "Ứng dụng người dùng được xây dựng bằng Flutter/Dart, kết nối với Backend ASP.NET Core Web API và MongoDB của hệ thống Moodify. Giao diện gồm bốn khu vực chính: Trang chủ, Khám phá, Lịch sử và Cá nhân. Dữ liệu tài khoản, danh mục nội dung, tâm trạng, kết quả gợi ý và đánh giá được trao đổi với API; luồng hoạt động chính không sử dụng dữ liệu mẫu thay thế khi máy chủ gặp lỗi."
      ],
      [
        "h",
        "1.2. Đăng ký, đăng nhập và thông tin cá nhân"
      ],
      [
        "p",
        "Người dùng có thể đăng ký tài khoản, đăng nhập và đăng xuất. Ứng dụng sử dụng token của phiên đăng nhập để gọi các API yêu cầu xác thực. Khi phiên không còn hợp lệ, người dùng được đưa về màn hình đăng nhập. Mục Cá nhân hiển thị thông tin tài khoản và cung cấp chức năng đăng xuất. Phiên hiện được giữ trong bộ nhớ ứng dụng, chưa duy trì đăng nhập sau khi đóng ứng dụng hoàn toàn."
      ],
      [
        "h",
        "1.3. Trang chủ và lựa chọn tâm trạng nhanh"
      ],
      [
        "p",
        "Khi vào trang chủ trong phiên đăng nhập, ứng dụng hiển thị hộp thoại hỏi cảm xúc hiện tại bằng các lựa chọn có emoji. Người dùng có thể chọn một tâm trạng, bỏ qua hoặc thay đổi lựa chọn sau đó. Danh sách tâm trạng được lấy từ máy chủ."
      ],
      [
        "p",
        "Sau khi xác nhận lựa chọn, trang chủ hiển thị tối đa ba bài hát và ba phim theo dạng thẻ ảnh bìa, kèm tên và thông tin ngắn. Mỗi thẻ dẫn đến màn hình chi tiết. Một lượt chọn gửi một yêu cầu gợi ý và tạo một bản ghi lịch sử. Trang chủ còn có nút chuyển đến Khám phá để người dùng mô tả hoặc tìm hiểu cảm xúc chi tiết hơn."
      ],
      [
        "h",
        "1.4. Khám phá tâm trạng"
      ],
      [
        "p",
        "Người dùng có thể chọn tâm trạng trực tiếp hoặc nhập mô tả ngắn. Ở giai đoạn hiện tại, mô tả được đối sánh bằng bộ từ khóa; người dùng xác nhận kết quả trước khi yêu cầu gợi ý. Ứng dụng hỗ trợ lựa chọn phim, nhạc hoặc cả hai. Chức năng này chưa sử dụng mô hình AI được huấn luyện."
      ]
    ]
  },
  {
    "title": "1. NHỮNG NỘI DUNG ĐÃ THỰC HIỆN (TIẾP)",
    "items": [
      [
        "h",
        "1.5. Gợi ý và khám phá danh mục"
      ],
      [
        "p",
        "Hệ thống sử dụng hai tọa độ cảm xúc Valence và Arousal để biểu diễn tâm trạng và nội dung. Backend lấy trung điểm vùng tâm trạng được chọn, tính khoảng cách đến nội dung và sắp xếp kết quả phù hợp. Ứng dụng hiển thị danh sách cùng mức phù hợp do API cung cấp. Chỉ số này thể hiện cách tính của thuật toán khoảng cách, không phải độ chính xác hay xác suất của AI."
      ],
      [
        "p",
        "Thư viện phim và nhạc hỗ trợ tìm kiếm theo thông tin nội dung, lọc thể loại và hiển thị thêm theo từng lô. Người dùng có thể mở chi tiết từ danh mục hoặc từ kết quả gợi ý."
      ],
      [
        "h",
        "1.6. Chi tiết phim và nghe nhạc"
      ],
      [
        "p",
        "Chi tiết phim hiển thị poster, tên phim, đạo diễn, thể loại, mô tả và điểm đánh giá khi dữ liệu có sẵn; có liên kết đến trang IMDb. Thông tin đạo diễn đã được bổ sung vào dữ liệu phim và luồng nhập metadata từ OMDb. Ứng dụng không lưu hoặc phát toàn bộ tệp phim."
      ],
      [
        "p",
        "Chi tiết nhạc hiển thị ảnh bìa, tên bài, nghệ sĩ, album, thời lượng và các thông tin liên quan. Spotify player được nhúng trực tiếp trong màn hình chi tiết để người dùng chủ động phát nhạc. Liên kết mở Spotify bên ngoài chỉ là lựa chọn bổ sung. Khả năng và thời lượng phát phụ thuộc nội dung cùng điều kiện của Spotify; ứng dụng không cam kết phát toàn bài hoặc lưu tệp nhạc để nghe ngoại tuyến."
      ],
      [
        "h",
        "1.7. Đánh giá nội dung"
      ],
      [
        "p",
        "Người dùng đánh giá từ một đến năm sao và nhập nhận xét trong màn hình chi tiết phim hoặc bài hát. Đánh giá được lưu qua API theo tài khoản và nội dung. Nếu đánh giá một sao, ứng dụng đề nghị người dùng khám phá tâm trạng khác. Phản hồi trong Web Admin có thể hiển thị tên, ảnh bìa và thông tin nội dung để quản trị viên nhận biết đối tượng được đánh giá."
      ],
      [
        "h",
        "1.8. Lịch sử và nhập liệu tiếng Việt"
      ],
      [
        "p",
        "Mục Lịch sử hiển thị các lần yêu cầu gợi ý, hỗ trợ tải thêm, lọc khoảng thời gian và biểu đồ tâm trạng từ dữ liệu đã tải. Các lượt gợi ý mới lưu danh sách nội dung tại thời điểm yêu cầu. Đây là lịch sử gợi ý, không phải bằng chứng người dùng đã nghe hoặc xem xong nội dung."
      ],
      [
        "p",
        "Ứng dụng cấu hình locale tiếng Việt và gợi ý bàn phím tiếng Việt cho tên người dùng, mô tả tâm trạng, tìm kiếm và nhận xét. Các trường nhập giữ văn bản Unicode và trạng thái đang ghép dấu. Mục Cá nhân có hướng dẫn bật bàn phím tiếng Việt. Kiểu gõ Telex/VNI do bàn phím hệ điều hành cung cấp; ứng dụng không tự cài đặt hoặc thay đổi bàn phím thiết bị."
      ]
    ]
  },
  {
    "title": "2. KẾT QUẢ ĐẠT ĐƯỢC",
    "items": [
      [
        "h",
        "2.1. Kết quả chức năng"
      ],
      [
        "p",
        "Đã hình thành luồng ứng dụng người dùng từ đăng ký, đăng nhập, lựa chọn hoặc mô tả tâm trạng, nhận gợi ý, xem chi tiết, mở trình phát nhạc, đánh giá đến xem lịch sử. Trang chủ cung cấp số lượng gợi ý ngắn gọn, đồng thời có lối chuyển sang khám phá chi tiết."
      ],
      [
        "p",
        "Mobile, Backend và Web Admin sử dụng chung dữ liệu nội dung và phản hồi qua API. Người dùng có thể khai thác danh mục phim/nhạc đã được quản trị nhập vào hệ thống. Các đánh giá từ mobile được lưu để quản trị viên theo dõi theo từng nội dung."
      ],
      [
        "p",
        "Đã có cơ chế gợi ý theo tọa độ cảm xúc để phục vụ thử nghiệm nghiệp vụ trước khi tích hợp AI. Cấu trúc ứng dụng phân tách giao tiếp API, mô hình dữ liệu, xử lý mô tả tâm trạng và giao diện, tạo cơ sở để bổ sung mô hình nhận diện cảm xúc sau này."
      ],
      [
        "h",
        "2.2. Kết quả kiểm tra"
      ],
      [
        "p",
        "Các lượt kiểm tra trước đã tạo thành công bản build Android debug và Flutter Web. Bộ kiểm thử tích hợp Backend ghi nhận 217 kiểm tra HTTP/nghiệp vụ thành công trên cơ sở dữ liệu kiểm thử độc lập, bao gồm các chức năng liên quan đến gợi ý, lịch sử, đạo diễn và phản hồi."
      ],
      [
        "p",
        "Flutter analyze không phát hiện vấn đề. Bộ kiểm thử Flutter sau khi bổ sung tiếng Việt gồm 19 kiểm thử đã thực thi thành công. Kiểm thử tập trung vào xử lý dữ liệu, lựa chọn tâm trạng, luồng gợi ý, chi tiết nhạc, đánh giá và quá trình ghép dấu tiếng Việt; không dùng kiểm thử giao diện để khẳng định đã phát âm thanh thực tế ổn định."
      ],
      [
        "h",
        "2.3. Phạm vi kết quả"
      ],
      [
        "p",
        "Ứng dụng đã đạt mức triển khai chức năng phục vụ kiểm thử và trình diễn đồ án. Chưa có đánh giá định lượng về độ chính xác nhận diện cảm xúc, chất lượng cá nhân hóa hoặc mức độ hài lòng trên tập người dùng đủ lớn. Các kết quả build và kiểm thử chức năng không đồng nghĩa hệ thống đã được kiểm chứng vận hành thương mại."
      ]
    ]
  },
  {
    "title": "3. KHÓ KHĂN VÀ 4. NỘI DUNG CHƯA THỰC HIỆN",
    "items": [
      [
        "h",
        "3.1. Dữ liệu và nhận diện cảm xúc"
      ],
      [
        "p",
        "Thông tin phim và nhạc đến từ nhiều nguồn nên mức độ đầy đủ không đồng nhất. Nhãn cảm xúc của nội dung hiện phụ thuộc cấu hình thể loại hoặc lựa chọn của quản trị viên. Việc xác định tâm trạng từ từ khóa còn hạn chế với ngữ cảnh, cách diễn đạt đa nghĩa và các cảm xúc xuất hiện đồng thời."
      ],
      [
        "h",
        "3.2. Môi trường thiết bị và dịch vụ phát nhạc"
      ],
      [
        "p",
        "Giả lập Android, Android Studio và tiến trình build cùng sử dụng tài nguyên máy tính, gây áp lực bộ nhớ trong quá trình thử nghiệm. Người dùng ghi nhận âm thanh preview trên giả lập có hiện tượng chói và cảm giác tua nhanh, trong khi cùng bài trên Admin nghe bình thường. Đã đo được tốc độ media 1x ở lần kiểm tra, nhưng chưa xác định đầy đủ nguyên nhân và chưa xác nhận âm thanh giả lập đã ổn định."
      ],
      [
        "p",
        "Spotify kiểm soát khả năng phát, thời lượng preview và các điều kiện liên quan tới phiên người nghe. Ngoài ra, cấu hình bàn phím tiếng Việt phụ thuộc hệ điều hành và bộ gõ được cài đặt; tại lượt bổ sung hiện tại chưa kiểm chứng Telex/VNI trên thiết bị đang kết nối."
      ],
      [
        "h",
        "4.1. Mô hình AI và chất lượng gợi ý"
      ],
      [
        "p",
        "Chưa tích hợp mô hình AI/NLP được huấn luyện để nhận diện cảm xúc; chưa triển khai nhận diện cảm xúc từ giọng nói hoặc hình ảnh. Chưa có pipeline huấn luyện, tập đánh giá chuẩn, đo độ chính xác mô hình hoặc cơ chế cá nhân hóa học từ phản hồi người dùng. Thuật toán hiện tại là gợi ý theo khoảng cách Valence/Arousal."
      ],
      [
        "h",
        "4.2. Hoàn thiện nghiệp vụ và vận hành"
      ],
      [
        "p",
        "Chưa có lưu phiên đăng nhập bền vững, chức năng chỉnh sửa hồ sơ/sở thích đồng bộ đầy đủ, nghe ngoại tuyến, phát phim hoặc theo dõi chính xác thời gian nghe/xem. Lịch sử hiện phản ánh yêu cầu gợi ý và phản hồi, không phản ánh hoàn thành trải nghiệm nội dung."
      ],
      [
        "p",
        "Chưa kiểm thử tải lớn hoặc triển khai production; danh mục hiện tải toàn bộ từ API rồi hiển thị theo lô ở ứng dụng. Chưa có xác minh đầy đủ trên điện thoại thật và iOS, chưa hoàn tất kiểm chứng âm thanh trên giả lập. Hỗ trợ tiếng Việt ở cấp ứng dụng đã được bổ sung, nhưng việc bật bộ gõ thực tế cần hoàn tất trên thiết bị sử dụng."
      ]
    ]
  }
];
const pages=sections.map((s,i)=>p(s.title,i===0?'Title':'Heading1')+s.items.map(([kind,text])=>kind==='h'?heading(text):p(text,kind==='subtitle'?'Subtitle':'Normal')).join(''));
fs.writeFileSync('docs/BAO_CAO_MOBILE_NGUOI_DUNG.md',sections.map(s=>'# '+s.title+'\n\n'+s.items.map(([k,t])=>(k==='h'?'## ':'')+t).join('\n\n')).join('\n\n'));
const styles=`<?xml version="1.0" encoding="UTF-8"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="Times New Roman"/><w:sz w:val="24"/><w:lang w:val="vi-VN"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="100" w:line="270" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>${[['Normal',24,'222222'],['Title',34,'17365D'],['Subtitle',24,'52647A'],['Heading1',29,'17365D'],['Heading2',25,'17365D'],['TableText',21,'222222']].map(([id,size,color])=>`<w:style w:type="paragraph" w:styleId="${id}"><w:name w:val="${id}"/><w:pPr>${id.startsWith('Heading')?'<w:keepNext/>':''}<w:spacing w:after="100"/></w:pPr><w:rPr><w:sz w:val="${size}"/><w:color w:val="${color}"/>${['Title','Heading1','Heading2'].includes(id)?'<w:b/>':''}</w:rPr></w:style>`).join('')}</w:styles>`;
fs.writeFileSync(out+'/word/styles.xml',styles);
fs.writeFileSync(out+'/word/document.xml',`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>${pages.join(br)}<w:sectPr><w:footerReference w:type="default" r:id="rId2"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1000" w:right="1000" w:bottom="1000" w:left="1300" w:footer="450"/></w:sectPr></w:body></w:document>`);
fs.writeFileSync(out+'/word/footer.xml','<?xml version="1.0" encoding="UTF-8"?><w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>Moodify · Ứng dụng người dùng | Trang </w:t></w:r><w:fldSimple w:instr="PAGE"/></w:p></w:ftr>');
fs.writeFileSync(out+'/word/_rels/document.xml.rels','<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer.xml"/></Relationships>');
fs.writeFileSync(out+'/_rels/.rels','<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>');
fs.writeFileSync(out+'/[Content_Types].xml','<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/footer.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>');
console.log('Prepared report: '+pages.length+' sections/pages, native editable Word text and tables.');
