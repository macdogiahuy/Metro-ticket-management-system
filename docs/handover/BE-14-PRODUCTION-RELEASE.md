# BE-14 — hồ sơ phát hành Neon production cho migration 014

**Trạng thái: BLOCKED / NOT DEPLOYED. Migration 014 chưa apply Neon production.**

Đây là biểu mẫu chuẩn bị phát hành, chưa phải bằng chứng đã backup, đã được
cấp quyền hoặc đã deploy. Chỉ điền dữ liệu thực sau khi thực hiện đúng
[quy trình release 014](../../infra/database/documentation/DEPLOYMENT_GUIDE.md#be-14-migration-014-production-release-gate).
Không ghi password, token hoặc connection string có secret trong hồ sơ.

## 1. Đích và phạm vi được phê duyệt

| Trường bắt buộc | Bằng chứng thực tế |
| --- | --- |
| Release ID | PENDING |
| Neon project ID / production branch ID / database | PENDING — phải xác minh lại, không suy từ hồ sơ release cũ |
| Git SHA và SHA-256 của `deploy.sql` | PENDING |
| Ledger trước deploy: version, name, checksum | PENDING |
| Danh sách migration còn thiếu sẽ được package apply | PENDING — gồm 010–013 nếu còn thiếu và 014 |
| Người cho phép tạo backup/rehearsal, thời điểm và tham chiếu xác nhận | PENDING |
| Người cho phép apply production, thời điểm và tham chiếu xác nhận riêng | PENDING |
| Cửa sổ deploy có timezone, operator thực hiện | PENDING |

Quyền deploy 007–009 trước đây, PR approval, kết quả test và yêu cầu sửa tài
liệu không thay thế authorization riêng cho release này. Thiếu bất kỳ trường
nào ở trên thì không apply production SQL. Nếu đích, package, phạm vi hoặc cửa
sổ triển khai thay đổi, phải cập nhật bằng chứng và xin authorization phù hợp.

## 2. Backup DB riêng trước deploy và kiểm chứng phục hồi

| Trường bắt buộc | Bằng chứng thực tế |
| --- | --- |
| Backup branch ID / tên branch mới | PENDING |
| Project và parent/source production branch ID | PENDING |
| Thời điểm tạo có timezone và snapshot LSN/time | PENDING |
| Retention của backup trong suốt nghiệm thu release | PENDING |
| Restore/rehearsal branch ID, khác production và backup | PENDING |
| Các bước phục hồi đã thực hiện và log kết quả | PENDING |
| Ledger/checksum và số liệu đối chiếu với baseline trước release | PENDING |
| Người xác minh và thời điểm kiểm chứng phục hồi | PENDING |

Git branch không phải backup DB. Backup release BE-04 cũ không đại diện cho
trạng thái trước release 014. Không apply package hoặc chạy fixture lên backup
được giữ lại; dùng branch phục hồi/rehearsal riêng để kiểm chứng.

## 3. Rehearsal và điều kiện trước deploy

- [ ] Đã xử lý số migration trùng với PR #90 và chốt package được review.
- [ ] `build_deploy.py --check` đạt; checksum package khớp mục 1.
- [ ] Rehearsal đúng package trên branch riêng đạt; lưu log và ledger sau apply.
- [ ] `09_verify.sql` trên rehearsal rc=0.
- [ ] Acceptance trên DB `metro_test_*` mới riêng đạt; lưu Git SHA, DB ID và TRX.
- [ ] Authorization riêng khớp đích, toàn bộ migration còn thiếu và cửa sổ deploy.
- [ ] Backup mới đúng nguồn còn được giữ lại; kiểm chứng phục hồi đạt.
- [ ] Operator đã kiểm lại đích, ledger và checksum ngay trước khi apply.

Thiếu một mục thì giữ **BLOCKED / NOT DEPLOYED**. Bằng chứng 240/240 test của
PR không thay thế backup hoặc authorization và không tự hoàn tất checklist này.

## 4. Kết quả production — chỉ điền sau khi được phép thực hiện

| Trường bắt buộc | Bằng chứng thực tế |
| --- | --- |
| Bắt đầu/kết thúc thực tế, operator, đích đã xác minh | NOT EXECUTED |
| Apply với `ON_ERROR_STOP=1`: exit code và log | NOT EXECUTED |
| Ledger/checksum sau apply và `09_verify.sql` | NOT EXECUTED |
| BE-14 smoke: phân quyền Admin/operator, ETA/tải, publish/resolve, gợi ý | NOT EXECUTED |
| Backup được giữ lại sau deploy; người nghiệm thu và tham chiếu | NOT EXECUTED |

Nếu SQL lỗi trước commit, transaction rollback; không tự retry hoặc khôi phục
production. Restore/chuyển branch sau commit cần authorization riêng và đối
soát dữ liệu phát sinh sau backup. Chỉ đổi trạng thái sang DEPLOYED khi đã có
đầy đủ bằng chứng thực tế và nghiệm thu.
