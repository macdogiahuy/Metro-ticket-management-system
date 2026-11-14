"""Published policy statements and separately labelled design recommendations."""
from .http_client import BASE

def rules(dataset):
    result=[]
    policies={p['tab_key']:p for p in dataset.get('ticket_policy_tabs',[])}
    def observed(name,description,entities,key,phrase,behavior,confidence='high'):
        policy=policies[key]
        content=(policy.get('basic_info_text') or '')+' '+(policy.get('usage_instructions_text') or '')
        start=content.lower().find(phrase.lower())
        quote=content[max(0,start-70):start+len(phrase)+230] if start>=0 else None
        result.append({'rule_id':f'BR_{len(result)+1:03d}','name':name,'description':description,'related_entities':entities,
                       'condition':description,'expected_behavior':behavior,'evidence_type':'observed_published_policy','confidence':confidence,
                       'evidence':{'record_id':policy['id'],'source_refs':policy['source_refs'],'quote':quote},'execution_verified':False})
    observed('Vé lượt đúng ngày và đúng hành trình','Vé lượt chỉ có giá trị trong ngày phát hành và đúng một hành trình.',['fare_products','tickets','ticket_validations'],'luot','trong ngày phát hành','Kiểm tra ngày dịch vụ, ga đi và ga đến.')
    observed('Giá vé phụ thuộc phương thức thanh toán','Chính sách công bố biểu giá tiền mặt và không tiền mặt khác nhau.',['fare_pairs','payments'],'luot','hai phương thức thanh toán','Cấu hình biểu giá theo phương thức; không suy ra đầy đủ giá cashless từ giá tiền mặt.')
    observed('Giá tiền mặt toàn tuyến','Chính sách nêu 19.000 VND tuyến 2A và 15.000 VND đoạn 8 ga tuyến 3.',['lines','fare_pairs'],'luot','19.000 VNĐ','Đối chiếu điểm đầu/cuối với bảng giá nguồn.')
    observed('Giá cashless được công bố','Chặng ngắn nhất 8.595 VND và toàn tuyến 2A 18.625 VND.',['fare_pairs','payments'],'luot','8.595 VNĐ','Giữ giá công bố; không tự tạo biểu giá cho mọi cặp ga.')
    observed('Trẻ dưới 6 tuổi đi cùng người lớn','Chính sách vé lượt miễn phí cho trẻ dưới 6 tuổi đi kèm người lớn.',['eligibility_verifications','tickets'],'luot','đi kèm người lớn','Kiểm tra điều kiện đi kèm và nhóm tuổi.')
    observed('Phải tap-in và tap-out','Hành khách quét/chạm phương tiện vé ở ga đi và ga đến.',['tickets','ticket_validations'],'luot','Ga đi (Tap-in)','Ghi nhận hai sự kiện theo một lượt đi.')
    observed('Đi quá ga phải xử lý bổ sung','Cổng ra cảnh báo khi quá cự ly; nhân viên hỗ trợ thanh toán chênh lệch.',['ticket_validations','payments'],'luot','Đi quá cự ly','Không tự mở cổng khi chặng thực tế chưa được xử lý.')
    observed('Mất vé QR','Chính sách yêu cầu liên hệ nhân viên và mua vé cao nhất của ga để kết thúc hành trình.',['tickets','payments'],'luot','nếu làm mất','Ghi nhận trường hợp hỗ trợ; không suy ra công thức bồi hoàn.')
    observed('Vé ngày và tuần không giảm theo nhóm ưu tiên','Chính sách ngày/tuần không áp dụng trợ giá học sinh, sinh viên, công nhân.',['fare_products','time_fares','eligibility_verifications'],'thoi_gian','không áp dụng chính sách giảm giá','Không áp dụng mặc định giảm 50% cho mọi loại vé.')
    observed('Vé ngày theo ngày hoạt động','Vé ngày có giá 40.000 VND và sử dụng không giới hạn trong ngày vận hành.',['fare_products','tickets'],'thoi_gian','ngày phát hành/kích hoạt','Hạn vé dựa trên ngày hoạt động đã xác nhận.')
    observed('Vé tuần bảy ngày liên tiếp','Vé tuần có giá 160.000 VND và hiệu lực bảy ngày từ phát hành/kích hoạt.',['fare_products','tickets'],'thoi_gian','07 ngày liên tiếp','Tách thời điểm mua và kích hoạt; xác nhận quy tắc ranh giới ngày.')
    observed('Cùng phương tiện ở cổng vào và ra','Chính sách yêu cầu dùng lại đúng mã QR hoặc phương tiện định danh đã dùng lúc vào.',['tickets','ticket_validations'],'thoi_gian','đúng mã QR','Liên kết sự kiện vào/ra bằng tham chiếu phương tiện vé.')
    observed('Nhóm ưu tiên vé tháng','Chính sách trợ giá 50% cho học sinh, sinh viên và công nhân khu công nghiệp.',['time_fares','eligibility_verifications'],'uu_dai','được trợ giá 50%','Nhóm ưu tiên cần kết quả xác minh phù hợp với sản phẩm.')
    observed('Giá vé tháng','Chính sách công bố 280.000 VND phổ thông và 140.000 VND ưu tiên.',['fare_products','time_fares'],'uu_dai','140.000 VNĐ','Lưu giá nguồn và phạm vi loại vé; không tạo giá mọi kỳ ưu tiên.')
    observed('Kích hoạt vé tháng app','Hạn vé tháng app tính từ ngày kích hoạt lần đầu theo tháng lịch trong ví dụ nguồn.',['tickets','fare_products'],'uu_dai','ngày kích hoạt sử dụng vé lần đầu','Giữ quy tắc tháng lịch; xử lý mâu thuẫn với nhãn 30 ngày trước khi triển khai.','medium')
    observed('Vé tháng NFC theo mua hoặc gia hạn','Chính sách NFC tính hiệu lực từ ngày mua hoặc gia hạn tại quầy.',['tickets'],'uu_dai','ngày mua hoặc gia hạn','Cấu hình quy tắc kích hoạt theo phương tiện vé.')
    observed('Vé tháng không chuyển nhượng','Vé tháng là vé định danh, không cho người khác mượn.',['tickets','passengers'],'uu_dai','không chuyển nhượng','Tách người trả tiền và người sử dụng nhưng gắn vé với người sử dụng.')
    observed('Mất thẻ vật lý không hoàn tiền','Chính sách mất thẻ vật lý không bảo toàn phần thời hạn còn lại và không hoàn tiền.',['tickets','refund_requests'],'uu_dai','không hoàn tiền','Áp dụng riêng trường hợp mất thẻ vật lý; không khái quát thành mọi vé không hoàn tiền.')
    observed('Vé tháng hết hạn hoặc lỗi','Cổng báo đỏ và không mở khi vé hết hạn hoặc lỗi thẻ.',['tickets','ticket_validations'],'uu_dai','Lỗi thẻ / Hết hạn vé','Nhân viên kiểm tra và hỗ trợ gia hạn/xử lý.')
    observed('Combo Metro–TNGo','Combo một tháng có giá 430.000 VND phổ thông và 290.000 VND ưu tiên.',['fare_products','offers'],'ve_combo_1_thang_metro_tngo','430 nghìn','Đề xuất sản phẩm combo riêng; chưa có bản ghi giá có cấu trúc trong API time.')
    observed('Giới hạn chuyến TNGo','Chính sách combo nêu mỗi chuyến xe đạp cơ tối đa 120 phút; quá hạn 3.000 điểm mỗi 15 phút.',['fare_products'],'ve_combo_1_thang_metro_tngo','120 phút','Đơn vị điểm của TNGo không được tự quy đổi thành VND.')
    result.append({'rule_id':f'BR_{len(result)+1:03d}','name':'Biểu mẫu phản ánh','description':'Họ tên và số điện thoại có dấu bắt buộc; tối đa 5 tệp, tổng <=10 MB.','related_entities':['feedback_cases','feedback_attachments'],'condition':'Gửi biểu mẫu phản ánh','expected_behavior':'Kiểm tra trường bắt buộc, số tệp và tổng dung lượng.','evidence_type':'observed_ui','confidence':'high','evidence':{'source_url':BASE+'contact/home','observed_at':'2026-11-06','quote':'Họ và tên *; Số điện thoại *; Tải lên tối đa 5 tệp. Tổng dung lượng nhỏ hơn hoặc bằng 10MB.'},'execution_verified':False})
    suggestions=[
     ('Giá chốt khi mua',['bookings','booking_items','time_fares'],'Chốt giá và quy tắc tại thời điểm mua; không tính lại lịch sử bằng giá mới.'),
     ('Phát hành vé sau thanh toán',['payments','tickets'],'Chỉ phát hành vé trả phí sau khi trạng thái thanh toán được máy chủ xác minh.'),
     ('Idempotency thanh toán',['payments','tickets'],'Mỗi tham chiếu hoặc idempotency key chỉ tạo một hiệu ứng phát hành.'),
     ('Xác thực nguyên tử',['tickets','ticket_validations'],'Hai lần quét đồng thời không được tiêu thụ một vé lượt hai lần.'),
     ('Giới hạn hoàn tiền',['payments','refund_requests'],'Tổng tiền hoàn thành công không vượt số tiền đã quyết toán.'),
     ('Tuyến–ga nhất quán',['line_stations','fare_pairs'],'Kiểm tra cả hai ga của giá thuộc tuyến tương ứng.'),
     ('Chưa xác nhận dữ liệu ưu tiên bằng 0',['fare_pairs'],'price_priority_raw=0 cần được xem xét; không coi là quyền miễn phí.'),
     ('Lịch tàu là ảnh chụp công bố',['scheduled_departures'],'Giữ ngày dịch vụ và nguồn; không suy ra dữ liệu live hoặc ID đoàn tàu.'),
     ('Tham chiếu tháng lịch',['tickets'],'Chủ nghiệp vụ phải xác nhận tháng lịch hay số ngày cố định và giờ hết hạn.'),
     ('Lưu giữ chứng cứ',['audit_events'],'Ghi lịch sử thay đổi và nguồn biểu giá phục vụ đối soát.')]
    for name,entities,behavior in suggestions:
        result.append({'rule_id':f'BR_{len(result)+1:03d}','name':name,'description':behavior,'related_entities':entities,'condition':'Triển khai hệ thống độc lập','expected_behavior':behavior,'evidence_type':'inferred_design_recommendation','confidence':'medium','evidence':{'basis':'Thiết kế giao dịch nhất quán theo miền vé metro; chưa xác minh trong backend của website.'},'execution_verified':False})
    return {'rules':result,'interpretation':'Observed means published statements or visible UI, not verified server enforcement.'}
