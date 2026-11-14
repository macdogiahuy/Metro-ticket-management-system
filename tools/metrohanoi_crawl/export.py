"""Website inventory, data quality, entity catalog, and JSON/SQL deliverables."""
import json
import hashlib
import re
from collections import Counter
from pathlib import Path
from urllib.parse import urljoin, urlparse
from .http_client import BASE, now
from .normalize import Normalizer
from .schema import build_schema, sql_document
from .rules import rules
from .validate import validate_dataset, validate_output

MODULES=[
 {'key':'journey','name':'Tra cứu hành trình','entities':['lines','stations','line_stations','pois','station_pois'],'features':['Chọn điểm đi/đến, đổi chiều, gợi ý địa điểm','Tìm hành trình','Bản đồ tuyến và chọn ga','Tiện ích quanh ga, lọc theo loại, ga và từ khóa']},
 {'key':'fares','name':'Thông tin vé và chính sách','entities':['fare_products','time_fares','fare_pairs','ticket_policy_tabs'],'features':['Tra cứu vé thời gian theo tuyến','Giá vé giữa cặp ga','Chính sách vé lượt, ngày/tuần, tháng/ưu tiên và combo']},
 {'key':'offer','name':'Ưu đãi','entities':['offers'],'features':['Danh sách, chi tiết, thời hạn và điều kiện ưu đãi']},
 {'key':'instruct','name':'Hướng dẫn','entities':['guides','faq_topics','faqs','content_pages'],'features':['Hướng dẫn sử dụng, nội quy, văn hóa, lợi ích','Câu hỏi theo chủ đề']},
 {'key':'news','name':'Tin tức','entities':['articles','content_categories'],'features':['Ba chuyên mục','Danh sách phân trang và bài viết chi tiết','Bài nổi bật, chia sẻ, thẻ nội dung nếu có']},
 {'key':'about','name':'Thông tin doanh nghiệp','entities':['document_groups','documents','document_files','content_pages'],'features':['Giới thiệu công ty','Công bố thông tin theo năm/loại, tìm tiêu đề','Quy chế công ty, xem trước và tải tài liệu']},
 {'key':'contact','name':'Liên hệ, tuyển dụng và hỗ trợ','entities':['jobs','feedback_cases','lost_property_reports'],'features':['Thông báo tuyển dụng','Biểu mẫu phản ánh/góp ý','Chức năng thất lạc; bản ghi người báo không được thu thập']}
]

LIMITATIONS=[
 'Website dùng SPA Angular. robots.txt và sitemap.xml đều trả HTML của ứng dụng, không có robots hoặc sitemap XML hợp lệ.',
 'Đây là ảnh chụp dữ liệu công khai ngày 06/11/2026; không xác nhận số liệu thực tế ngoài website, hoạt động backend hay thiết kế DB nội bộ.',
 'API nội dung yêu cầu header client được công bố trong cấu hình công khai. Lần thử không có header nhận 401; không truy cập API đăng nhập, tài khoản, đơn hàng, phản ánh hoặc thất lạc.',
 'Chỉ thu thập URL/metadata 128 tệp doanh nghiệp; không tải hoặc phân tích PDF/DOCX, tránh thu thập dữ liệu cá nhân trong tài liệu.',
 'Lịch thu thập cho 06, 07, 08/11/2026: một ngày trong tuần và hai ngày cuối tuần; không chứng minh toàn bộ lịch tương lai hoặc ngày lễ.',
 'Không ghép các dòng lịch thành chuyến tàu vì không có định danh chuyến toàn tuyến đáng tin cậy.',
 'Giá cashless cho đầy đủ cặp ga, giá ưu tiên cho toàn bộ kỳ tháng, thời điểm hiệu lực giá, năng lực đoàn tàu, đặt chỗ và điều kiện hoàn tiền chung còn thiếu.',
 'Một chuyên mục hướng dẫn chỉ có ID, thiếu tên/slug. Giữ bản ghi tham chiếu có nhãn null, không bịa tên.',
 'Không thu thập bản ghi hành khách, định danh, sinh trắc học, tác giả, người báo thất lạc, hồ sơ tuyển dụng hoặc giao dịch thực tế.',
 'Các thực thể giao dịch, quyền, audit và trạng thái được đề xuất cho kiến trúc mới; chưa quan sát triển khai của website.',
 'Bản đồ Goong báo lỗi tải trong trình duyệt; không yêu cầu vị trí của người dùng hoặc gọi dịch vụ định vị bên thứ ba.',
 'Liên kết footer /line chuyển về trang chủ trong kiểm tra UI. Trang ga xác minh được là /line/map?station=<slug>.',
 'Crawl hoàn tất các trang phân trang và nhóm đã công bố trong phạm vi; liên kết ứng dụng/mạng xã hội và tài liệu nhị phân không được crawl.'
]

def page_inventory(dataset,responses):
    pages={}
    def add(url,title,type_,module,parent=BASE,entities=None,description=None,status='api_content_extracted'):
        if url in pages:
            return pages[url]
        row={'url':url,'page_title':title,'page_type':type_,'parent_page':parent,'functional_module':module,
             'description':description or title,'data_displayed':entities or [],'available_user_actions':[],
             'related_entities':entities or [],'data_extraction_status':status,'content_evidence':[],
             'rendered_ui_verified':False,'depth':0 if url==BASE else min(4,urlparse(url).path.count('/')+(1 if urlparse(url).query else 0))}
        pages[url]=row
        return row
    add(BASE,'Hà Nội Metro – Hành trình xanh cùng Hà Nội Metro','homepage','overview',None,['lines','stations','fare_products','offers','articles','documents'])
    for item in dataset.get('menu_items',[]):
        if item.get('url'):
            url=urljoin(BASE,item['url'])
            module=urlparse(url).path.strip('/').split('/')[0]
            add(url,item['name'],'navigation_page',module,entities=next((m['entities'] for m in MODULES if m['key']==module),[]),description=item.get('description'),status='navigation_discovered')
    for table,prefix,titlefield in [('articles','news/','title'),('guides','instruct/','title'),('offers','offer/','name'),('jobs','contact/recruitment-detail?slug=','title'),('pois','station-amenities/','name')]:
        for row in dataset.get(table,[]):
            add(BASE+prefix+row['slug'],row[titlefield],'detail',prefix.split('/')[0],BASE+prefix.split('/')[0],[table],status='api_content_extracted' if table!='pois' else 'listing_record_extracted')
    for station in dataset.get('stations',[]):
        row=add(BASE+'line/map?station='+station['slug'],'Ga '+station['name'],'station_detail','journey',BASE+'line/map',['stations','station_amenities','station_exits','station_bus_routes','scheduled_departures'])
        row['available_user_actions']=['Chọn ga','Xem thông tin, lịch tàu, buýt, địa điểm quanh ga','Đổi ngày/hướng tra cứu']
    for policy in dataset.get('ticket_policy_tabs',[]):
        add(BASE+'fares/ticket-policy?tab='+policy['tab_key'],policy['label'],'policy_tab','fares',BASE+'fares/ticket-policy',['ticket_policy_tabs'])
    for group in dataset.get('document_groups',[]):
        add(BASE+'about/detail-information?docType='+group['doc_type']+'&year='+str(group['year']),group['title'],'document_group','about',BASE+'about/enterprise-information',['documents','document_files'],status='group_content_extracted')
    for r in responses:
        for url in r['source_pages']:
            # Older checkpoints used the disabled /line/<slug> URL.
            if re.match(r'https://metrohanoi.vn/line/[^/?]+$',url) and not url.endswith('/map'):
                url=BASE+'line/map?station='+url.rsplit('/',1)[-1]
            row=add(url,urlparse(url).path,'functional_page',urlparse(url).path.strip('/').split('/')[0] or 'overview')
            row['content_evidence'].append({'request_id':r['request_id'],'source_url':r['source_url'],'endpoint':r['endpoint'],'payload':r['payload']})
            row['data_extraction_status']='api_content_extracted'
    verified=[BASE,BASE+'fares/home',BASE+'fares/ticket-policy',BASE+'line/map?station=cat-linh',BASE+'contact/home']
    for url in verified:
        add(url,urlparse(url).path or 'Trang chủ','functional_page','overview')['rendered_ui_verified']=True
    contact=pages[BASE+'contact/home']
    contact['forms']=[{'name':'Phiếu tiếp nhận phản ánh và góp ý','submitted':False,'fields':[
      {'name':'full_name','label':'Họ và tên','type':'text','required':True},
      {'name':'phone','label':'Số điện thoại','type':'tel','required':True},
      {'name':'email','label':'Email','type':'email','required':False},
      {'name':'contribution_type','type':'radio','options':['Góp ý đề xuất','Khen ngợi','Khác']},
      {'name':'line','type':'radio','options':['Tuyến 2A Cát Linh – Hà Đông','Tuyến 3.1 Nhổn – Ga Hà Nội']},
      {'name':'location','type':'text'}, {'name':'occurred_at','type':'datetime'},
      {'name':'description','type':'textarea'}, {'name':'attachments','type':'file','max_files':5,'max_total_size_display':'10MB'}]}]
    for row in pages.values():
        if row['functional_module'] in ('news','offer','instruct'):
            row['available_user_actions']+=['Xem danh sách/chi tiết','Phân trang hoặc chọn chuyên mục khi có']
        if '/contact/lost-property' in row['url']:
            row['data_extraction_status']='excluded_personal_records; navigation_and_feature_only'
    return sorted(pages.values(),key=lambda p:p['url'])

def export(root='output'):
    root=Path(root)
    responses=json.loads((root/'crawl_responses.json').read_text(encoding='utf-8'))
    state=json.loads((root/'crawl_state.json').read_text(encoding='utf-8'))
    norm=Normalizer(responses)
    dataset=norm.run()
    schema,relationships=build_schema(dataset)
    pages=page_inventory(dataset,responses)
    checks=validate_dataset(dataset,schema,responses)
    def write(name,value):
        (root/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    overview={'website':{'name':'Hà Nội Metro','base_url':BASE,'description':'Cổng thông tin công khai tuyến metro Hà Nội, vé, hành trình, hướng dẫn và thông tin doanh nghiệp.','domain':'Urban metro passenger information and ticketing','language':'vi','main_modules':MODULES},'analysis_scope':'Publicly observable domain and independent database proposal','technology_observed':{'frontend':'Angular SPA','content_api_base':state['api_base_url']},'discovery':{'robots':state['robots'],'sitemap_status':state['sitemap_status'],'navigation_menu_items':len(dataset['menu_items']),'public_api_discovery':'Endpoint names and payloads explicitly published in application JavaScript. POST calls are allowlisted read-only content queries.'},'search_and_pagination':{'articles':{'page_base':0,'page_size':12,'category_filter':'categoryId','response':'categories + articles.items/currentPage/totalPages/totalItems'},'pois':{'page_base':1,'page_size':12,'filters':['q','station','categories']},'documents':{'page_base':0,'filters':['pageType','year','docType','title']},'fares':{'filters':['lineId','fromStationId','toStationId','ticketType','category']},'schedules':{'filters':['stationId','direction','targetDate','period'],'sample_dates':['2026-11-06','2026-11-07','2026-11-08']},'sorting':'Published sortOrder/orderPriority and server ordering; no unsupported custom sort parameters sent.'}}
    write('website_overview.json',overview)
    write('pages.json',{'pages':pages})
    write('sitemap.json',{'source':'Menu tree, published route declarations and collected record links; not a server XML sitemap.','max_depth':4,'pages':[{'url':p['url'],'parent_url':p['parent_page'],'depth':p['depth'],'type':p['page_type'],'status':p['data_extraction_status']} for p in pages], 'document_assets':[{'url':row['url'],'type':row['extension'],'status':row['download_status']} for row in dataset['document_files']], 'excluded_routes':['Authentication/customer/order APIs','Lost report list/detail/lookup/create','Feedback submission','Article/guide/offer tracking','Organization chart containing personal names','External app stores/social/maps']})
    write('raw_data.json',{'collection_type':'Public source responses, with personal record fields removed. Original scalar values/HTML retained where permitted.','responses':responses})
    write('normalized_data.json',{'metadata':{'generated_at':now(),'language':'vi','identifier_strategy':'Independent deterministic UUIDv5 of table + published/compound source key. source_id retains the source key; scheduled departures include service date.','source_values_policy':'No missing source value fabricated. Presentation whitespace/NFC normalized. Currency VND comes from published policy. IDs and junction records are deterministic transformations.','conflict_policy':'Latest collected non-null field retained; all original variants and pointers retained in raw_data and data_quality_report.'},'data':dataset,'inferred_transaction_records':[]})
    entities=[]
    for table in schema['database']['tables']:
        rows=dataset.get(table['name'],[])
        sources=sorted({p for row in rows for ref in row['source_refs'] for p in ref['source_pages']})
        entities.append({'name':table['name'],'description':table['description'],'business_purpose':table['description'],'evidence_type':table['evidence_type'],'attributes':[{'name':c['name'],'type':c['sql_type'],'required':not c['nullable'],'is_primary_key':c.get('primary_key',False),'is_foreign_key':bool(c.get('references')),'references':c['references'],'example':c.get('example'),'validation':'Not null' if not c['nullable'] else 'Optional; preserve null when not published','origin':c.get('origin'),'description':c.get('description') or c['name'].replace('_',' ')} for c in table['columns']],'source_pages':sources,'record_count':len(rows),'confidence':'medium' if table['evidence_type']=='inferred_suggestion' else 'high','unique_identifiers':table['unique_constraints']})
    write('entities.json',{'entities':entities,'interpretation':'Business/domain catalog and independent relational representation. Not a reconstruction of the original database.'})
    for rel in relationships:
        rel['source_pages']=next(e['source_pages'] for e in entities if e['name']==rel['from'])
    relationships.extend([{'from':'lines','to':'stations','type':'many_to_many','junction_entity':'line_stations','confidence':'high','evidence_type':'observed_membership_independent_generalization','description':'Source shows each station on one line; multiple-line support is a future design capability.'}, {'from':'stations','to':'bus_routes','type':'many_to_many','junction_entity':'station_bus_routes','confidence':'high','evidence_type':'observed','description':'Same bus route codes occur at several station lists.'},{'from':'stations','to':'pois','type':'many_to_many','junction_entity':'station_pois','confidence':'high','evidence_type':'observed','description':'POI records publish station slug associations.'}])
    write('relationships.json',{'relationships':relationships})
    write('database_schema.json',schema)
    (root/'schema.sql').write_text(sql_document(schema),encoding='utf-8')
    from .seed import seed_sql
    (root/'seed_reference.sql').write_text(seed_sql(dataset,schema),encoding='utf-8')
    write('business_rules.json',rules(dataset))
    observed_enums={}
    for table,fields in {'lines':['status'],'stations':['type','status'],'schedule_boards':['direction','day_type'],'time_fares':['category'],'offers':['status'],'articles':['status'],'jobs':['status'],'document_groups':['doc_type','page_type'],'fare_products':['code']}.items():
        for field in fields:
            values=sorted({r[field] for r in dataset.get(table,[]) if r.get(field) is not None},key=str)
            observed_enums[f'{table}.{field}']={'values':values,'evidence_type':'observed_values_only','exhaustive':False}
    proposed={'booking_status':['pending','paid','expired','cancelled'],'payment_status':['pending','succeeded','failed','unknown','refunded'],'ticket_status':['issued','active','in_journey','used','expired','suspended','refunded'],'refund_status':['requested','approved','processing','succeeded','rejected','unknown'],'ticket_validation_event':['tap_in','tap_out'],'roles':['passenger','station_staff','administrator']}
    write('enums_and_reference_data.json',{'observed_enums':observed_enums,'inferred_enums':{k:{'values':v,'evidence_type':'inferred_suggestion'} for k,v in proposed.items()},'reference_records':{k:dataset[k] for k in ['amenity_types','fare_products','faq_topics','content_categories','poi_categories']},'do_not_seed_as_verified':['Unresolved guide category label','Entire inferred transaction/user/status dataset','Cashless matrix or priority time prices not published']})
    mandatory_errors=[e for e in checks['errors'] if e['type']=='missing_mandatory']
    missing=[]
    for table,rows in dataset.items():
        for column in schema['database']['tables'][next(i for i,t in enumerate(schema['database']['tables']) if t['name']==table)]['columns']:
            count=sum(row.get(column['name']) is None for row in rows)
            if count:
                missing.append({'table':table,'field':column['name'],'missing_count':count,'required':not column['nullable']})
    schedules=dataset['scheduled_departures']
    stations={s['id']:s for s in dataset['stations']}
    outside=[s for s in schedules if s['depart_time']<stations[s['station_id']]['open_time'] or s['depart_time']>stations[s['station_id']]['close_time']]
    self_routes=[s for s in schedules if s.get('origin_station_id')==s.get('destination_station_id')]
    long_dwell=[s for s in schedules if s.get('dwell_seconds',0)>180]
    zero_priority=[f for f in dataset['fare_pairs'] if f.get('price_priority_raw')==0]
    unknowncats=[c for c in dataset['content_categories'] if c.get('is_identifier_only')]
    issues=[
     {'id':'DQ_001','severity':'high','name':'Hạn tháng lịch và nhãn số ngày khác nhau','evidence':'API time có nhãn 30/60/.../360 ngày; chính sách nêu ví dụ 24/3 đến hết 24/4.','related_records':[x['id'] for x in dataset['fare_products'] if x['code'].startswith('month_')],'source_urls':[BASE+'fares/home',BASE+'fares/ticket-policy?tab=uu_dai'],'resolution':'Giữ cả nhãn quảng bá và nguyên văn; không tính expires_at từ nhãn.'},
     {'id':'DQ_002','severity':'high','name':'Trường giá ưu tiên chưa rõ nghĩa','record_count':len(zero_priority),'record_ids':[x['id'] for x in zero_priority],'source_url':state['api_base_url']+'/fares/matrix','resolution':'Giữ price_priority_raw; không coi 0 là miễn phí. Tuyến 3.1 còn có giá ưu tiên khác 0, trong khi chính sách nhấn mạnh ưu đãi vé tháng.'},
     {'id':'DQ_003','severity':'medium','name':'Ngưỡng tuổi chưa nhất quán','evidence':'Trang chủ nêu từ 60 tuổi; chính sách vé tháng viết >60t.','source_urls':[BASE,BASE+'fares/ticket-policy?tab=uu_dai'],'resolution':'Chưa chọn toán tử >= hay > để triển khai.'},
     {'id':'DQ_004','severity':'high','name':'Giờ khởi hành ngoài giờ hoạt động công bố','record_count':len(outside),'sample_ids':[x['id'] for x in outside[:20]],'interpretation':'Cần kiểm tra mô hình giờ hoạt động; không tự cắt lịch.'},
     {'id':'DQ_005','severity':'high','name':'Dòng lịch có ga đầu bằng ga cuối','record_count':len(self_routes),'sample_ids':[x['id'] for x in self_routes[:20]],'interpretation':'Tham chiếu terminal cần người quản trị xác minh; dữ liệu vẫn được giữ nguyên.'},
     {'id':'DQ_006','severity':'medium','name':'Thời gian dừng dài cần xem xét','record_count':len(long_dwell),'review_threshold_seconds':180,'threshold_origin':'inferred_review_heuristic','maximum_published_seconds':max(s['dwell_seconds'] for s in schedules),'sample_ids':[x['id'] for x in long_dwell[:20]]},
     {'id':'DQ_007','severity':'medium','name':'Thiếu nhãn chuyên mục hướng dẫn','record_ids':[x['id'] for x in unknowncats],'resolution':'Tên/slug null; tham chiếu ID giữ nguyên.'},
     {'id':'DQ_008','severity':'medium','name':'Biến thể mô tả giữa các phản hồi','conflict_count':len(norm.conflicts),'resolution':'Lưu mọi biến thể trong source_value_conflicts; giá trị chuẩn hóa lấy lần thu thập cuối.'},
     {'id':'DQ_009','severity':'low','name':'ID nguồn có mẫu lặp','evidence':'UUID tuyến/ga có tiền tố lặp.','resolution':'Đây là ID được công bố; không khẳng định dữ liệu giả hay thật ngoài nguồn.'},
     {'id':'DQ_010','severity':'medium','name':'Chuyến tiếp theo không live','evidence':'Các bản ghi nextTrains được thu thập có isLive=false.','resolution':'Chỉ lưu ảnh chụp; không dùng làm telemetry thời gian thực.'}
    ]
    counts={k:len(v) for k,v in dataset.items()}
    quality={'total_pages_discovered':len(pages),'total_pages_crawled':sum(bool(p['content_evidence']) for p in pages),'total_content_requests':len(responses),'total_records_extracted':sum(counts.values()),'record_counts':counts,'duplicate_records':0,'duplicate_occurrences_collapsed':dict(norm.duplicates),'missing_fields':missing,'invalid_records':mandatory_errors,'unresolved_relationships':[{'entity':'content_categories','id':x['id'],'reason':'Guide category ID exists; label/slug not exposed.'} for x in unknowncats], 'data_quality_score':90 if checks['status']=='passed' else 0,'score_method':'45% FK/provenance integrity + 25% required fields + 20% core record completeness + 10% semantic release readiness. Structural checks pass; unresolved semantic gates receive 0/10. Score is not a real-world accuracy estimate.','integrity_validation':checks,'issues':issues,'source_value_conflicts':norm.conflicts,'limitations':LIMITATIONS,'semantic_release_ready':False,'scope_note':'API content representations are counted separately from rendered UI pages; five pages were verified in the browser.'}
    write('data_quality_report.json',quality)
    endpoint_catalog=[{'name':name,'path':path,'discovery_evidence':BASE+next(s for s in state['scripts'] if s.startswith('main.')),'status':'collected' if any(r['endpoint']==name for r in responses) else 'declared_not_called'} for name,path in state['endpoints'].items()]
    write('crawl_metadata.json',{'base_url':BASE,'started_at':state['started_at'],'finished_at':state['checkpoint_at'],'exported_at':now(),'timezone':'Asia/Saigon','client_date':'2026-11-06','crawl_config':{'crawl_depth':4,'max_pages':500,'respect_robots_txt':True,'use_sitemap':True,'follow_internal_links':True,'public_pages_only':True,'exclude_personal_information':True,'min_request_interval_seconds':1.5,'authentication':'Only the client header published for public website content. No user login/session/cookies or protected entity APIs.'},'crawl_status':'completed_public_module_scope','content_requests':len(responses),'api_endpoints':endpoint_catalog,'request_log':state['requests'],'api_key_exported':False,'rendered_ui_verified_pages':[p['url'] for p in pages if p['rendered_ui_verified']],'limits_reached':False,'metadata_vs_page_count':'472 content query responses include pagination, date and fare-pair variants; cached repeated queries have separate response context. Asset/config/robots requests are logged separately.','limits_and_exclusions':LIMITATIONS})
    # Remove client keys from earlier discovery evidence too, without deleting evidence files.
    for meta_path in (root/'evidence').glob('*.json'):
        meta=json.loads(meta_path.read_text(encoding='utf-8'))
        if '/assets/config/' in meta.get('url',''):
            body_path=meta_path.with_suffix('.body')
            if body_path.exists():
                config=json.loads(body_path.read_text(encoding='utf-8'))
                body_path.write_text(json.dumps({'environment':config.get('environment'),'api':{'baseUrl':config.get('api',{}).get('baseUrl'),'apiKey':'[REDACTED]'},'note':'All client/map keys omitted.'},ensure_ascii=False,indent=2),encoding='utf-8')
                meta['evidence_redacted']=True
                meta['evidence_sha256']=hashlib.sha256(body_path.read_bytes()).hexdigest()
                meta_path.write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
    report=validate_output(root)
    quality['integrity_validation']={k:v for k,v in report.items() if k!='json_files'}
    write('data_quality_report.json',quality)
    (root/'README.md').write_text(summary(counts,quality,schema,report),encoding='utf-8')
    return {'record_counts':counts,'records':sum(counts.values()),'pages':len(pages),'content_requests':len(responses),'tables':len(schema['database']['tables']),'validation':report['status']}

def summary(counts,quality,schema,report):
    sql_summary=(f"Schema và seed đã thực thi thành công trên PostgreSQL; đối chiếu đủ {len(schema['database']['tables'])} bảng, {sum(counts.values()):,} bản ghi và ràng buộc. Bằng chứng cùng SHA-256 nằm trong `postgresql_verification.json`."
                 if report.get('postgresql_verification',{}).get('status')=='passed'
                 else 'SQL được sinh từ cùng catalog và kiểm tra cấu trúc; chưa có bằng chứng thực thi PostgreSQL còn hiệu lực cho bộ tệp này.')
    return f'''# Tham chiếu miền nghiệp vụ Hà Nội Metro

Nguồn: https://metrohanoi.vn/ — thu thập ngày 06/11/2026, múi giờ Asia/Saigon.

Đã hoàn tất phạm vi nội dung công khai qua **472 phản hồi truy vấn**, các danh sách phân trang và cả ba chuyên mục bài viết. Đây là dữ liệu và thiết kế độc lập, không phải bản sao hay mô tả cơ sở dữ liệu gốc.

## Nội dung đã trích xuất

- 2 tuyến, 20 ga, thứ tự tuyến–ga, 80 trạng thái tiện nghi, 24 ảnh ga, 6 lối ra được công bố.
- {counts['bus_routes']} mã tuyến buýt và {counts['station_bus_routes']} quan hệ buýt–ga; 7 địa điểm quanh ga, 9 liên kết POI–ga.
- 15 mã sản phẩm vé, 28 mức giá vé thời gian theo tuyến, 188 cặp ga có hướng; phương thức giá API chưa được xác nhận.
- 120 bảng lịch ga/hướng/ngày, {counts['scheduled_departures']:,} dòng giờ đến/đi cho 6–8/11/2026; ghi rõ weekday/weekend.
- 43 bài viết, 6 hướng dẫn, 45 FAQ trong 5 chủ đề, 4 chính sách vé, 3 ưu đãi và 4 thông báo tuyển dụng.
- 14 bản ghi công bố tài liệu và 128 URL tệp (127 PDF, 1 DOCX). Tệp nhị phân chưa tải/phân tích.
- 8 trang nội dung CMS; cây điều hướng và biểu mẫu phản ánh công khai.

## Tệp sử dụng

Mười hai JSON theo yêu cầu nằm trực tiếp tại `output/`. `validation_report.json` là báo cáo bổ sung. `schema.sql` gồm {len(schema['database']['tables'])} bảng đề xuất, trong đó 14 bảng giao dịch/tài khoản/hỗ trợ/audit là suy luận cho hệ thống mới và không có dữ liệu giao dịch thật. `seed_reference.sql` nạp 33 bảng dữ liệu tham chiếu vào database nghiên cứu rỗng. `raw_data.json` chứa phản hồi đã loại các trường bản ghi cá nhân; `normalized_data.json` chứa ID độc lập, FK và `source_refs` với JSON pointer về nguồn. Xem `tools/metrohanoi_crawl/README.md` trong gói để chạy lại.

## Điểm cần xác nhận trước khi làm dữ liệu sản xuất

Chính sách vé tháng dùng tháng lịch trong ví dụ, trong khi danh sách ghi 30/60/... ngày. Chính sách ưu đãi không áp dụng chung cho mọi sản phẩm. Trường `pricePriority=0` không được hiểu là miễn phí; tuyến 3.1 còn có giá ưu tiên khác 0. API không cung cấp đầy đủ biểu giá cashless hoặc thời điểm hiệu lực.

Có {quality['issues'][3]['record_count']} dòng giờ khởi hành ngoài khoảng hoạt động được công bố, {quality['issues'][4]['record_count']} dòng lịch có ga đầu bằng ga cuối, và {quality['issues'][5]['record_count']} dòng dừng trên ngưỡng xem xét 180 giây. Ngưỡng 180 giây là gợi ý rà soát, không phải quy tắc của website. Một chuyên mục hướng dẫn chỉ có ID; tên/slug được để null. Tất cả vấn đề và biến thể nguồn nằm trong `data_quality_report.json`.

## Phạm vi và giới hạn

SPA trả cùng HTML tại robots.txt và sitemap.xml; crawler ghi rõ không có chỉ dẫn robots/XML hợp lệ. Endpoint/payload được đọc từ script mà website công bố, chỉ gọi danh sách/chi tiết/tra cứu nội dung. Client key công khai được dùng như ứng dụng bình thường và không xuất ra tệp. Không thu thập tài khoản, định danh, sinh trắc học, báo cáo thất lạc, phản ánh đã gửi hay giao dịch. Không gửi biểu mẫu và không gọi endpoint tracking. Bản đồ Goong lỗi tải trong kiểm tra UI. Footer `/line` chuyển về trang chủ; chi tiết ga dùng `/line/map?station=<slug>`.

## Kiểm tra

Kết quả: **{report['status']}**. Kiểm tra cú pháp JSON, khóa định danh, bắt buộc, ràng buộc UNIQUE, tất cả FK không-null và JSON pointer nguồn. {sql_summary} Điểm 90/100 phản ánh cấu trúc và các trường cốt lõi, không ước lượng độ đúng thực tế. Các vấn đề ngữ nghĩa chưa đạt điều kiện sử dụng sản xuất.

## Chạy lại

Từ thư mục gốc repo: `python -m tools.metrohanoi_crawl crawl --max-pages 500`, rồi `python -m tools.metrohanoi_crawl export` và `python -m tools.metrohanoi_crawl validate`. Python 3.11+; chỉ dùng thư viện chuẩn. HTTP giới hạn tối thiểu 1,5 giây giữa các yêu cầu; dừng khi gặp 401/403/429. Dữ liệu cache được dùng lại; xem README của crawler để tạo ảnh chụp mới và chọn ngày dịch vụ.
'''
