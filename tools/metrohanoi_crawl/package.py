"""Finalize reports and create a checksummed delivery archive without local secrets."""
import hashlib
import json
import zipfile
from pathlib import Path
from .export import summary
from .validate import validate_output


def refresh_evidence(root):
    """Backfill stored-body hashes after field removal; original response hashes remain intact."""
    hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'evidence').glob('*.body')}

    def refresh(value):
        if isinstance(value,dict):
            if 'evidence_path' in value:
                name=Path(value['evidence_path']).name
                if name in hashes:
                    value['evidence_sha256']=hashes[name]
            for nested in value.values():
                refresh(nested)
        elif isinstance(value,list):
            for nested in value:
                refresh(nested)

    paths=list((root/'evidence').glob('*.json'))
    paths.extend(root/name for name in ('crawl_responses.json','crawl_state.json','discovery.json','crawl_metadata.json','raw_data.json'))
    for path in paths:
        if path.exists():
            value=json.loads(path.read_text(encoding='utf-8'))
            refresh(value)
            # Older configuration metadata may not have an evidence_path.
            if path.parent==root/'evidence' and path.with_suffix('.body').name in hashes:
                value['evidence_sha256']=hashes[path.with_suffix('.body').name]
            path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')


def build_archive(root='output',name='metrohanoi-reference-2026-11-06.zip'):
    root=Path(root).resolve()
    source=Path(__file__).resolve().parent
    refresh_evidence(root)
    report=validate_output(root)
    if report['status']!='passed':
        raise ValueError('Dataset validation failed; archive not created.')
    quality=json.loads((root/'data_quality_report.json').read_text(encoding='utf-8'))
    quality['integrity_validation']={k:v for k,v in report.items() if k!='json_files'}
    (root/'data_quality_report.json').write_text(json.dumps(quality,ensure_ascii=False,indent=2),encoding='utf-8')
    schema=json.loads((root/'database_schema.json').read_text(encoding='utf-8'))
    (root/'README.md').write_text(summary(quality['record_counts'],quality,schema,report),encoding='utf-8')
    report=validate_output(root)
    archive=root/name
    files=[(p,'output/'+p.relative_to(root).as_posix()) for p in root.rglob('*')
           if p.is_file() and p.suffix not in ('.zip','.sha256') and p.name!='delivery_manifest.json']
    files.extend((p,'tools/metrohanoi_crawl/'+p.relative_to(source).as_posix()) for p in source.rglob('*')
                 if p.is_file() and p.suffix in ('.py','.md') and '__pycache__' not in p.parts)
    files.append((source.parent/'.gitignore','tools/.gitignore'))
    files.sort(key=lambda pair:pair[1])
    manifest={'source_website':'https://metrohanoi.vn/','snapshot_date':'2026-11-06',
              'validation':report['status'],'postgresql':report.get('postgresql_verification',{}).get('status','not_tested'),
              'files':[{'path':label,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p,label in files],
              'excluded':['Temporary PostgreSQL cluster and credentials','Python bytecode','Existing application source and unrelated local changes','Archive and archive checksum itself']}
    manifest_path=root/'delivery_manifest.json'
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as bundle:
        for path,label in files:
            bundle.write(path,label)
        bundle.write(manifest_path,'output/delivery_manifest.json')
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip():
            raise ValueError('Archive CRC validation failed.')
        for item in manifest['files']:
            if hashlib.sha256(bundle.read(item['path'])).hexdigest()!=item['sha256']:
                raise ValueError('Archive content hash differs from the delivery manifest.')
        if any('.verification/' in path or '__pycache__/' in path or 'pg-password' in path for path in bundle.namelist()):
            raise ValueError('A temporary file was included in the archive.')
    checksum=hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.zip.sha256').write_text(f'{checksum}  {archive.name}\n',encoding='utf-8')
    return {'archive':str(archive),'bytes':archive.stat().st_size,'files':len(files)+1,'sha256':checksum,'validation':'passed'}


if __name__=='__main__':
    print(json.dumps(build_archive(),ensure_ascii=False,indent=2))
