"""An independent PostgreSQL proposal, never a claim about the source database."""
import re

DESCRIPTIONS={
 'menu_items':'Cây điều hướng website; mục menu không đồng nghĩa với một thực thể nghiệp vụ.',
 'lines':'Tuyến đường sắt và trạng thái được công bố.', 'stations':'Nhà ga, địa chỉ, tọa độ và giờ hoạt động.',
 'line_stations':'Quan hệ tuyến–ga có thứ tự; hỗ trợ ga giao nhau trong thiết kế mới.',
 'line_time_slots':'Khoảng thời gian tra cứu lịch trên từng tuyến.',
 'amenity_types':'Danh mục tiện nghi được hiển thị tại ga.', 'station_amenities':'Trạng thái có/không của từng tiện nghi tại ga.',
 'station_images':'Ảnh công khai của ga; chỉ lưu URL.', 'station_exits':'Lối ra được công bố; không suy ra lối ra còn thiếu.',
 'bus_routes':'Tuyến buýt theo mã tuyến công bố.', 'station_bus_routes':'Tuyến buýt gần từng ga và mô tả hướng đi.',
 'next_train_observations':'Ảnh chụp thông tin chuyến tiếp theo; is_live=false không phải dữ liệu thời gian thực.',
 'schedule_boards':'Bảng lịch theo ga, hướng và ngày dịch vụ.', 'scheduled_departures':'Giờ đến/đi công bố; chưa liên kết thành chuyến tàu toàn tuyến.',
 'fare_products':'Danh mục loại vé từ thông tin công khai, chưa quyết định ngày hết hạn.',
 'time_fares':'Giá vé thời gian theo tuyến và nhóm hành khách được API công bố.',
 'fare_pairs':'Giá vé cho cặp ga có hướng; phương thức thanh toán chưa được API xác định.',
 'fare_path_stations':'Thứ tự ga trong từng phản hồi tra cứu giá; lưu cả request vì thứ tự phụ thuộc truy vấn.',
 'ticket_policy_tabs':'Chính sách vé nguyên văn và bản văn bản chuẩn hóa.', 'policy_guide_steps':'Bước hướng dẫn thuộc chính sách vé.',
 'faq_topics':'Nhóm câu hỏi thường gặp.', 'faqs':'Câu hỏi và câu trả lời công khai.',
 'content_categories':'Chuyên mục nội dung; có một tham chiếu chỉ biết ID của hướng dẫn.',
 'articles':'Bài viết công khai và nội dung; không thu thập bản ghi tác giả.', 'guides':'Hướng dẫn hành khách.',
 'tags':'Nhãn bài viết.', 'article_tags':'Quan hệ bài viết–nhãn.',
 'offers':'Chương trình ưu đãi; trạng thái nguồn và thời hạn phải được kiểm tra độc lập.',
 'jobs':'Thông báo tuyển dụng; không thu thập hồ sơ ứng viên.',
 'content_pages':'Nội dung CMS dạng văn bản/JSON công khai.',
 'document_groups':'Nhóm tài liệu theo trang, loại, năm và tiêu đề; không dùng riêng loại+năm làm khóa duy nhất.',
 'documents':'Bản ghi công bố tài liệu, có thể chứa nhiều tệp.', 'document_files':'Từng URL tệp; tách trường URL nối bằng dấu chấm phẩy.',
 'poi_categories':'Loại địa điểm quanh ga.', 'pois':'Địa điểm tiện ích/điểm tham quan công khai.',
 'station_pois':'Quan hệ nhiều–nhiều giữa ga và địa điểm quanh ga.'
}

FK={
 'menu_items':{'parent_id':'menu_items'}, 'line_stations':{'line_id':'lines','station_id':'stations'},
 'line_time_slots':{'line_id':'lines'}, 'station_amenities':{'station_id':'stations','amenity_type_id':'amenity_types'},
 'station_images':{'station_id':'stations'}, 'station_exits':{'station_id':'stations'},
 'station_bus_routes':{'station_id':'stations','bus_route_id':'bus_routes'},
 'next_train_observations':{'station_id':'stations'}, 'schedule_boards':{'station_id':'stations'},
 'scheduled_departures':{'board_id':'schedule_boards','line_id':'lines','station_id':'stations','origin_station_id':'stations','destination_station_id':'stations'},
 'time_fares':{'line_id':'lines','fare_product_id':'fare_products'},
 'fare_pairs':{'line_id':'lines','from_station_id':'stations','to_station_id':'stations'},
 'fare_path_stations':{'fare_pair_id':'fare_pairs','station_id':'stations'},
 'policy_guide_steps':{'policy_tab_id':'ticket_policy_tabs'}, 'faqs':{'topic_id':'faq_topics'},
 'articles':{'category_id':'content_categories'},'guides':{'category_id':'content_categories'},
 'article_tags':{'article_id':'articles','tag_id':'tags'}, 'documents':{'group_id':'document_groups'},
 'document_files':{'document_id':'documents'},'pois':{'category_id':'poi_categories'},
 'station_pois':{'station_id':'stations','poi_id':'pois'}
}

REQUIRED={
 'lines':['code','name'], 'stations':['slug','name'], 'line_stations':['line_id','station_id','sequence'],
 'amenity_types':['code','name'], 'station_amenities':['station_id','amenity_type_id'],
 'station_exits':['station_id','exit_code','name'], 'bus_routes':['code','name'],
 'station_bus_routes':['station_id','bus_route_id'], 'schedule_boards':['station_id','direction','service_date'],
 'scheduled_departures':['board_id','line_id','station_id','service_date','depart_time'],
 'fare_products':['code'], 'time_fares':['line_id','fare_product_id','price','currency'],
 'fare_pairs':['line_id','from_station_id','to_station_id','price_adult','currency'],
 'fare_path_stations':['fare_pair_id','station_id','request_id','published_sequence'],
 'faq_topics':['name'], 'faqs':['topic_id','question','answer'],
 'articles':['slug','title'], 'guides':['slug','title'], 'offers':['slug','name'], 'jobs':['slug','title'],
 'content_pages':['key','content_original'], 'documents':['title'], 'document_files':['document_id','url'],
 'pois':['slug','name'], 'station_pois':['station_id','poi_id'], 'ticket_policy_tabs':['tab_key','label']
}

UNIQUES={
 'lines':[['code']], 'stations':[['slug']], 'line_stations':[['line_id','station_id'],['line_id','sequence']],
 'amenity_types':[['code']], 'station_amenities':[['station_id','amenity_type_id']],
 'station_exits':[['station_id','exit_code']], 'bus_routes':[['code']],
 'station_bus_routes':[['station_id','bus_route_id']], 'schedule_boards':[['station_id','direction','service_date']],
 'fare_products':[['code']], 'fare_path_stations':[['fare_pair_id','request_id','published_sequence']],
 'articles':[['slug']], 'guides':[['slug']], 'offers':[['slug']], 'jobs':[['slug']],
 'content_pages':[['key']], 'document_files':[['document_id','url']], 'pois':[['slug']],
 'station_pois':[['station_id','poi_id']], 'ticket_policy_tabs':[['tab_key']], 'tags':[['name']], 'article_tags':[['article_id','tag_id']]
}

EXTENSIONS={
 'passengers':('Tài khoản hành khách đề xuất cho hệ thống mới; dữ liệu cá nhân không được crawl.',{'display_name':'text','identity_status':'text','deleted_at':'timestamptz'}),
 'roles':('Vai trò đề xuất từ phạm vi dự án, không phải vai trò xác minh của website.',{'code':'text','name':'text'}),
 'passenger_roles':('Phân quyền nhiều–nhiều đề xuất.',{'passenger_id':'uuid','role_id':'uuid'}),
 'bookings':('Đơn mua vé đề xuất; website chỉ giới thiệu mua vé trên ứng dụng.',{'passenger_id':'uuid','status':'text','total_amount':'numeric(14,2)','currency':'char(3)','expires_at':'timestamptz'}),
 'booking_items':('Chi tiết đơn với giá chốt tại thời điểm mua.',{'booking_id':'uuid','fare_product_id':'uuid','from_station_id':'uuid','to_station_id':'uuid','quantity':'integer','unit_amount':'numeric(14,2)','fare_snapshot':'jsonb'}),
 'payments':('Giao dịch thanh toán đề xuất; không thu thập giao dịch thật.',{'booking_id':'uuid','provider':'text','provider_reference':'text','idempotency_key':'text','status':'text','amount':'numeric(14,2)','currency':'char(3)'}),
 'tickets':('Vé và hạn sử dụng đề xuất; tách ngày mua, kích hoạt, hết hạn.',{'booking_item_id':'uuid','passenger_id':'uuid','status':'text','token_hash':'text','issued_at':'timestamptz','activated_at':'timestamptz','expires_at':'timestamptz','validity_policy_snapshot':'jsonb'}),
 'ticket_validations':('Lịch sử vào/ra đề xuất dựa trên hướng dẫn tap-in/tap-out.',{'ticket_id':'uuid','station_id':'uuid','event_type':'text','occurred_at':'timestamptz','result':'text','idempotency_key':'text'}),
 'refund_requests':('Quy trình hoàn tiền tùy chọn; điều kiện chung chưa được công bố.',{'payment_id':'uuid','ticket_id':'uuid','status':'text','amount':'numeric(14,2)','reason':'text','provider_refund_reference':'text'}),
 'eligibility_verifications':('Kết quả xác minh ưu tiên đề xuất; chỉ lưu trạng thái và tham chiếu, không sao chép CCCD/sinh trắc học.',{'passenger_id':'uuid','eligibility_code':'text','status':'text','verified_at':'timestamptz','expires_at':'timestamptz','external_reference':'text'}),
 'feedback_cases':('Thực thể hỗ trợ đề xuất từ biểu mẫu công khai; không gửi biểu mẫu.',{'line_id':'uuid','submitter_name':'text','submitter_phone':'text','submitter_email':'text','contribution_type':'text','location':'text','occurred_at':'timestamptz','description':'text','status':'text'}),
 'feedback_attachments':('Tệp đính kèm đề xuất; biểu mẫu giới hạn 5 tệp và tổng 10 MB.',{'feedback_case_id':'uuid','storage_reference':'text','media_type':'text','byte_size':'bigint'}),
 'lost_property_reports':('Mô hình tùy chọn từ chức năng thất lạc; không thu thập bản ghi hay thông tin người báo.',{'station_id':'uuid','public_code':'text','item_description':'text','status':'text','reported_at':'timestamptz'}),
 'audit_events':('Nhật ký bất biến đề xuất cho thay đổi giá, thanh toán và xác thực.',{'entity_type':'text','entity_id':'uuid','action':'text','occurred_at':'timestamptz','change_summary':'jsonb'})
}
EXT_FK={
 'passenger_roles':{'passenger_id':'passengers','role_id':'roles'}, 'bookings':{'passenger_id':'passengers'},
 'booking_items':{'booking_id':'bookings','fare_product_id':'fare_products','from_station_id':'stations','to_station_id':'stations'},
 'payments':{'booking_id':'bookings'}, 'tickets':{'booking_item_id':'booking_items','passenger_id':'passengers'},
 'ticket_validations':{'ticket_id':'tickets','station_id':'stations'}, 'refund_requests':{'payment_id':'payments','ticket_id':'tickets'},
 'eligibility_verifications':{'passenger_id':'passengers'}, 'feedback_cases':{'line_id':'lines'},
 'feedback_attachments':{'feedback_case_id':'feedback_cases'},'lost_property_reports':{'station_id':'stations'}
}
EXT_REQUIRED={
 'roles':['code','name'], 'bookings':['status','total_amount','currency'],
 'booking_items':['quantity','unit_amount','fare_snapshot'], 'payments':['provider','idempotency_key','status','amount','currency'],
 'tickets':['status','token_hash','issued_at','validity_policy_snapshot'],
 'ticket_validations':['event_type','occurred_at','result','idempotency_key'],
 'refund_requests':['status','amount'], 'eligibility_verifications':['eligibility_code','status'],
 'feedback_cases':['submitter_name','submitter_phone'], 'feedback_attachments':['storage_reference','byte_size'],
 'lost_property_reports':['public_code','item_description','status'], 'audit_events':['entity_type','entity_id','action','occurred_at']
}

def field_type(name,values,foreign=False):
    if name=='id' or foreign:
        return 'uuid'
    if name in ['source_id','source_schedule_id','request_id']:
        return 'text'
    if name=='currency':
        return 'char(3)'
    actual=[v for v in values if v is not None]
    if any(isinstance(v,(list,dict)) for v in actual) or name in ['source_refs','structured_content']:
        return 'jsonb'
    if actual and all(isinstance(v,bool) for v in actual):
        return 'boolean'
    if name in ['lat','lng']:
        return 'numeric(10,7)'
    if 'price' in name:
        return 'numeric(14,2)'
    if name in ['service_date','start_date','end_date','published_date','effective_from','effective_to']:
        return 'date'
    if name.endswith('_at'):
        return 'timestamptz'
    if name in ['open_time','close_time','arrive_time','depart_time','departure_time']:
        return 'time'
    if actual and all(isinstance(v,int) and not isinstance(v,bool) for v in actual):
        return 'bigint' if name=='view_count' else 'integer'
    if actual and all(isinstance(v,(float,int)) for v in actual):
        return 'numeric'
    return 'text'

def build_schema(dataset):
    tables=[]
    relationships=[]
    indexes=[]
    constraints=[]
    for table,rows in dataset.items():
        keys=set().union(*(row.keys() for row in rows)) if rows else set()
        fks=FK.get(table,{})
        cols=[]
        for field in sorted(keys,key=lambda k:(k!='id',k)):
            values=[row.get(field) for row in rows]
            required=field in ['id','source_id','evidence_type','source_refs']+REQUIRED.get(table,[])
            cols.append({'name':field,'sql_type':field_type(field,values,field in fks),'nullable':not required,
                         'primary_key':field=='id','references':{'table':fks[field],'column':'id'} if field in fks else None,
                         'description':'Nguồn trích xuất và JSON pointer.' if field=='source_refs' else 'ID độc lập UUIDv5; không phải ID nội bộ của cơ sở dữ liệu gốc.' if field=='id' else None,
                         'example':next((v for v in values if v is not None),None),
                         'origin':'generated_identifier' if field=='id' else 'extraction_metadata' if field in ['source_refs','evidence_type'] else 'normalized_public_value'})
        tables.append({'name':table,'description':DESCRIPTIONS.get(table,table),'evidence_type':'observed_domain_independent_design','columns':cols,'unique_constraints':[['source_id']]+UNIQUES.get(table,[]),'audit_fields':[{'name':'ingested_at','sql_type':'timestamptz','default':'CURRENT_TIMESTAMP'}],'soft_delete':'Không áp dụng cho ảnh chụp nguồn; giữ lịch sử thu thập.'})
    for table,(description,fields) in EXTENSIONS.items():
        columns=[{'name':'id','sql_type':'uuid','nullable':False,'primary_key':True,'references':None,'origin':'inferred_suggestion'}]
        for name,type_ in fields.items():
            ref=EXT_FK.get(table,{}).get(name)
            columns.append({'name':name,'sql_type':type_,'nullable':name not in EXT_FK.get(table,{}) and name not in EXT_REQUIRED.get(table,[]),'primary_key':False,'references':{'table':ref,'column':'id'} if ref else None,'origin':'inferred_suggestion'})
        unique={'roles':[['code']],'passenger_roles':[['passenger_id','role_id']],'payments':[['provider','provider_reference'],['idempotency_key']],'tickets':[['token_hash']],'ticket_validations':[['idempotency_key']],'lost_property_reports':[['public_code']]}.get(table,[])
        tables.append({'name':table,'description':description,'evidence_type':'inferred_suggestion','columns':columns,'unique_constraints':unique,'audit_fields':[{'name':'created_at','sql_type':'timestamptz','default':'CURRENT_TIMESTAMP'},{'name':'updated_at','sql_type':'timestamptz','default':'CURRENT_TIMESTAMP'}],'soft_delete':'Chỉ tài khoản/hồ sơ hỗ trợ theo chính sách lưu giữ; giao dịch và nhật ký dùng trạng thái/lịch sử.'})
    for table in tables:
        for col in table['columns']:
            if col['references']:
                relationships.append({'from':table['name'],'to':col['references']['table'],'type':'many_to_one','foreign_key':col['name'],'mandatory':not col['nullable'],'references':'id','confidence':'medium' if table['evidence_type']=='inferred_suggestion' else 'high','evidence_type':table['evidence_type'],'description':table['description'],'evidence':'Tham chiếu trong dữ liệu chuẩn hóa; lựa chọn khóa ngoại cho thiết kế độc lập.'})
                indexes.append({'name':f'ix_{table["name"]}_{col["name"]}','table':table['name'],'columns':[col['name']],'method':'btree'})
        for unique in table['unique_constraints']:
            constraints.append({'table':table['name'],'type':'unique','columns':unique})
    checks=[('stations','latitude_range','lat BETWEEN -90 AND 90'),('stations','longitude_range','lng BETWEEN -180 AND 180'),('line_stations','positive_sequence','sequence > 0'),('time_fares','nonnegative_price','price >= 0'),('fare_pairs','nonnegative_adult_price','price_adult >= 0'),('fare_pairs','different_stations','from_station_id <> to_station_id'),('time_fares','valid_effective_range','effective_to IS NULL OR effective_from IS NULL OR effective_to > effective_from'),('fare_pairs','valid_effective_range','effective_to IS NULL OR effective_from IS NULL OR effective_to > effective_from'),('feedback_attachments','nonnegative_bytes','byte_size >= 0'),('payments','nonnegative_amount','amount >= 0'),('refund_requests','nonnegative_amount','amount >= 0'),('booking_items','positive_quantity','quantity > 0')]
    for table,name,expression in checks:
        constraints.append({'table':table,'name':'ck_'+table+'_'+name,'type':'check','expression':expression})
    indexes.extend([{'name':'ix_departure_board_time','table':'scheduled_departures','columns':['board_id','depart_time'],'method':'btree'}, {'name':'ix_fare_pair_lookup','table':'fare_pairs','columns':['line_id','from_station_id','to_station_id'],'method':'btree'}, {'name':'ix_articles_publication','table':'articles','columns':['category_id','published_at'],'method':'btree'}])
    return {'database':{'recommended_engine':'PostgreSQL','schema_name':'metro_reference','implementation_claim':False,'tables':tables,'indexes':indexes,'constraints':constraints,
        'normalization_recommendations':[
         'Tách tuyến–ga có thứ tự, ga–tiện nghi, ga–buýt, ga–POI thành bảng nối; không giới hạn ga chỉ thuộc một tuyến.',
         'Giữ UUID nguồn trong source_id; dùng UUIDv5 độc lập làm PK. Không coi UUID có mẫu lặp là chứng cứ dữ liệu nghiệp vụ đã được kiểm định.',
         'Tách nhiều URL tài liệu thành document_files. Không phân loại bản ghi tài liệu chỉ theo doc_type+year vì có nhiều tiêu đề cùng loại/năm.',
         'Bảng scheduled_departures là ảnh chụp theo ngày; chưa suy ra train_trip_id từ giờ đi hoặc ID nguồn.',
         'Giá tham chiếu giữ phương thức thanh toán và ngày hiệu lực null nếu không được công bố. Phiên bản giá sản xuất cần khóa nghiệp vụ và exclusion constraint khoảng hiệu lực sau khi chủ sở hữu xác nhận quy tắc.',
         'Không diễn giải price_priority_raw=0 thành miễn phí. Nhóm ưu tiên và điều kiện áp dụng cần nguồn xác nhận.',
         'JSONB phù hợp với nội dung CMS, chứng cứ trích xuất, ảnh chụp quy tắc tại thời điểm mua; FK, tiền và thời gian tra cứu đặt ở cột riêng.',
         'Các tên tuyến/ga lặp trong lịch được bỏ khỏi dữ liệu chuẩn hóa; tra tên qua FK. Giữ nguyên trong raw_data.',
         'Phân biệt hạn theo ngày hoạt động, số ngày và tháng lịch. Giải quyết mâu thuẫn nguồn trước khi tạo vé thật.',
         'Trạng thái giao dịch là đề xuất, không phải trạng thái quan sát. updated_at cần cơ chế cập nhật của ứng dụng hoặc trigger; mặc định chỉ thiết lập lúc INSERT.'
        ],'production_constraints_requiring_application_or_trigger':[
         'Cặp ga của vé phải cùng tuyến: dùng khóa ngoại tổng hợp tới line_stations hoặc kiểm tra trong transaction.',
         'Cửa vào/ra cần cập nhật nguyên tử; vé lượt không thể được tiêu thụ hai lần.',
         'Tổng tiền hoàn không vượt khoản thanh toán đã quyết toán; xử lý webhook và refund idempotent.',
         'Đính kèm biểu mẫu tối đa 5 tệp, tổng <=10 MB; kiểm tra tổng trong giao dịch.',
         'Xác nhận enum và vòng đời vé/thanh toán với chủ nghiệp vụ trước khi ràng buộc sản xuất.'
        ]}},relationships

def sql_document(schema):
    db=schema['database']
    lines=['-- Independent proposal generated from public domain evidence.', '-- Reference tables and optional inferred application tables. No live database is modified.', 'BEGIN;', 'CREATE SCHEMA IF NOT EXISTS metro_reference;', 'SET LOCAL search_path = metro_reference, public;']
    for table in db['tables']:
        parts=[]
        for column in table['columns']:
            text='"'+column['name']+'" '+column['sql_type']
            if column.get('primary_key'):
                text+=' PRIMARY KEY'
            elif not column['nullable']:
                text+=' NOT NULL'
            parts.append(text)
        for field in table['audit_fields']:
            parts.append('"'+field['name']+'" '+field['sql_type']+' NOT NULL DEFAULT '+field['default'])
        for unique in table['unique_constraints']:
            parts.append('UNIQUE ('+', '.join('"'+c+'"' for c in unique)+')')
        lines.append('CREATE TABLE "'+table['name']+'" (\n  '+',\n  '.join(parts)+'\n);')
    for table in db['tables']:
        for column in table['columns']:
            ref=column['references']
            if ref:
                lines.append(f'ALTER TABLE "{table["name"]}" ADD CONSTRAINT "fk_{table["name"]}_{column["name"]}" FOREIGN KEY ("{column["name"]}") REFERENCES "{ref["table"]}" ("{ref["column"]}") ON DELETE RESTRICT;')
    for check in db['constraints']:
        if check['type']=='check':
            lines.append(f'ALTER TABLE "{check["table"]}" ADD CONSTRAINT "{check["name"]}" CHECK ({check["expression"]});')
    for index in db['indexes']:
        lines.append(f'CREATE INDEX "{index["name"]}" ON "{index["table"]}" USING {index["method"]} ('+', '.join('"'+c+'"' for c in index['columns'])+');')
    lines.append('COMMIT;')
    return '\n\n'.join(lines)+'\n'
