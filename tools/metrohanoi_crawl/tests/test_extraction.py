import copy
import json
import tempfile
import unittest
import urllib.request
from pathlib import Path
from tools.metrohanoi_crawl.postgres_verify import ARTIFACTS, artifact_hashes, verified_evidence
from tools.metrohanoi_crawl.http_client import SameOriginRedirect
from tools.metrohanoi_crawl.normalize import identifier, normalized
from tools.metrohanoi_crawl.validate import resolve_pointer, validate_dataset

class ExtractionTests(unittest.TestCase):
    def test_unicode_and_unknown_values(self):
        self.assertEqual(normalized({'nameEn':'  Cát   Linh ','address':''}),{'name_en':'Cát Linh','address':None})
        url='https://metrohanoi.vn/path/Cát_Linh.jpg'
        self.assertEqual(normalized(url),url)

    def test_service_dates_keep_source_id_distinct(self):
        self.assertNotEqual(identifier('scheduled_departures','source:2026-11-06'),identifier('scheduled_departures','source:2026-11-07'))
        self.assertEqual(identifier('stations','cat-linh'),identifier('stations','cat-linh'))

    def test_nested_pointer_and_array(self):
        doc={'data':[{'fileList':['https://metrohanoi.vn/a.pdf']}],'a/b':{'~k':3}}
        self.assertEqual(resolve_pointer(doc,'/data/0/fileList/0'),'https://metrohanoi.vn/a.pdf')
        self.assertEqual(resolve_pointer(doc,'/a~1b/~0k'),3)

    def test_external_redirect_rejected_before_request(self):
        request=urllib.request.Request('https://metrohanoi.vn/')
        with self.assertRaises(urllib.error.URLError):
            SameOriginRedirect().redirect_request(request,None,302,'Found',{},'https://example.org/')

    def test_validation_detects_orphan_and_missing_provenance(self):
        dataset={'parents':[{'id':identifier('parents','p'),'source_id':'p','evidence_type':'observed','source_refs':[]}], 'children':[{'id':identifier('children','c'),'source_id':'c','evidence_type':'observed','source_refs':[],'parent_id':identifier('parents','missing')}]}
        cols=lambda ref:[{'name':'id','references':None},{'name':'source_id','references':None}, *([{'name':'parent_id','references':{'table':'parents','column':'id'}}] if ref else [])]
        schema={'database':{'tables':[{'name':'parents','columns':cols(False),'unique_constraints':[['source_id']]},{'name':'children','columns':cols(True),'unique_constraints':[['source_id']]}],'indexes':[]}}
        result=validate_dataset(dataset,schema,[])
        self.assertEqual(result['status'],'failed')
        self.assertIn('foreign_key_missing',{e['type'] for e in result['errors']})
        self.assertIn('untraceable_record',{e['type'] for e in result['errors']})

    def test_sql_evidence_invalidated_when_artifact_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for name in ARTIFACTS:
                (root/name).write_text('{}',encoding='utf-8')
            dataset={'parents':[{'id':'p'}]}
            schema={'database':{'tables':[{'name':'parents'}]}}
            evidence={'status':'passed','artifact_sha256':artifact_hashes(root),'actual_record_counts':{'parents':1}}
            (root/'postgresql_verification.json').write_text(json.dumps(evidence),encoding='utf-8')
            self.assertIsNotNone(verified_evidence(root,dataset,schema))
            (root/'seed_reference.sql').write_text('-- changed',encoding='utf-8')
            self.assertIsNone(verified_evidence(root,dataset,schema))

if __name__=='__main__':
    unittest.main()
