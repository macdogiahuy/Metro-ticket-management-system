"""Check artifacts, provenance pointers, required values, and all generated FKs."""
import json
import hashlib
import re
import uuid
from collections import Counter
from pathlib import Path
from .schema import REQUIRED

def resolve_pointer(document,pointer):
    value=document
    for component in pointer.lstrip('/').split('/'):
        if not component:
            continue
        component=component.replace('~1','/').replace('~0','~')
        value=value[int(component)] if isinstance(value,list) else value[component]
    return value

def validate_dataset(dataset,schema,responses):
    errors=[]
    counts=Counter()
    tables={t['name']:t for t in schema['database']['tables']}
    ids={k:{row['id'] for row in rows} for k,rows in dataset.items()}
    request_map={r['request_id']:r for r in responses}
    for table,rows in dataset.items():
        if len(ids[table])!=len(rows):
            errors.append({'type':'duplicate_primary_key','table':table})
        sourcekeys=[row['source_id'] for row in rows]
        if len(set(sourcekeys))!=len(sourcekeys):
            errors.append({'type':'duplicate_source_key','table':table})
        for row in rows:
            counts['records_checked']+=1
            try:
                uuid.UUID(row['id'])
            except ValueError:
                errors.append({'type':'invalid_uuid','table':table,'id':row['id']})
            for name in ['id','source_id','source_refs','evidence_type']+REQUIRED.get(table,[]):
                counts['mandatory_fields_checked']+=1
                if row.get(name) in (None,'',[]):
                    errors.append({'type':'missing_mandatory','table':table,'id':row['id'],'field':name})
            for col in tables[table]['columns']:
                if not re.fullmatch(r'[a-z][a-z0-9_]*',col['name']):
                    errors.append({'type':'invalid_column_name','name':col['name']})
                ref=col.get('references')
                value=row.get(col['name'])
                if ref and value is not None:
                    counts['foreign_keys_checked']+=1
                    if value not in ids.get(ref['table'],set()):
                        errors.append({'type':'foreign_key_missing','table':table,'id':row['id'],'field':col['name'],'value':value})
            for unique in tables[table]['unique_constraints']:
                counts['unique_constraints_checked']+=1
            if not row.get('source_refs'):
                errors.append({'type':'untraceable_record','table':table,'id':row['id']})
            for ref in row.get('source_refs',[]):
                counts['provenance_pointers_checked']+=1
                try:
                    source=request_map[ref['request_id']]
                    resolve_pointer(source['response'],ref['json_pointer'])
                    if ref['source_url']!=source['source_url']:
                        raise ValueError('URL mismatch')
                except (KeyError,IndexError,ValueError,TypeError):
                    errors.append({'type':'invalid_source_pointer','table':table,'id':row['id'],'ref':ref})
        for unique in tables[table]['unique_constraints']:
            values=[tuple(row.get(c) for c in unique) for row in rows if all(row.get(c) is not None for c in unique)]
            if len(set(values))!=len(values):
                errors.append({'type':'unique_constraint_violation','table':table,'columns':unique})
    for table in schema['database']['tables']:
        cols={c['name'] for c in table['columns']}
        for col in table['columns']:
            if col.get('references') and col['references']['table'] not in tables:
                errors.append({'type':'schema_foreign_table_missing','table':table['name'],'field':col['name']})
        for unique in table['unique_constraints']:
            if not set(unique).issubset(cols):
                errors.append({'type':'schema_unique_column_missing','table':table['name'],'columns':unique})
    for index in schema['database']['indexes']:
        if not set(index['columns']).issubset({c['name'] for c in tables[index['table']]['columns']}):
            errors.append({'type':'index_column_missing','index':index['name']})
    return {'status':'passed' if not errors else 'failed','checks':dict(counts),'errors':errors,
            'foreign_key_validation_scope':'Every non-null FK in normalized reference records; schema target/column consistency.',
            'original_vs_inferred':'Reference records contain source_refs. Inferred application tables have no collected records.',
            'sql_execution':'DDL generated and structurally checked; not executed against a PostgreSQL instance.'}

def validate_output(root='output'):
    root=Path(root)
    syntax=[]
    for path in sorted(root.glob('*.json')):
        try:
            json.loads(path.read_text(encoding='utf-8'))
            syntax.append({'file':path.name,'status':'passed'})
        except (ValueError,UnicodeError) as exc:
            syntax.append({'file':path.name,'status':'failed','error':str(exc)})
    dataset=json.loads((root/'normalized_data.json').read_text(encoding='utf-8'))['data']
    schema=json.loads((root/'database_schema.json').read_text(encoding='utf-8'))
    responses=json.loads((root/'raw_data.json').read_text(encoding='utf-8'))['responses']
    report=validate_dataset(dataset,schema,responses)
    evidence_checks=0
    for path in (root/'evidence').glob('*.json'):
        meta=json.loads(path.read_text(encoding='utf-8'))
        body=path.with_suffix('.body')
        if body.exists():
            evidence_checks+=1
            if meta.get('evidence_sha256')!=hashlib.sha256(body.read_bytes()).hexdigest():
                report['errors'].append({'type':'evidence_body_hash_mismatch','file':path.name})
    report['checks']['evidence_body_hashes_checked']=evidence_checks
    if report['errors']:
        report['status']='failed'
    from .postgres_verify import verified_evidence
    evidence=verified_evidence(root,dataset,schema)
    if evidence:
        report['sql_execution']='Passed on PostgreSQL: schema and seed loaded; all table counts and declared constraints verified.'
        report['postgresql_verification']={'file':'postgresql_verification.json','status':'passed','verified_at':evidence['verified_at'],'engine_version':evidence['engine_version'],'artifact_sha256':evidence['artifact_sha256']}
    elif (root/'postgresql_verification.json').exists():
        report['sql_execution']='SQL verification evidence is failed or stale; rerun against an empty research database.'
    report['json_files']=syntax
    if any(x['status']=='failed' for x in syntax):
        report['status']='failed'
    (root/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    return report
