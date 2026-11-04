# Tra cứu nhanh lệnh Git

| Command | Ý nghĩa | Khi nào sử dụng |
|---|---|---|
| `git clone` | Clone repository | Lần đầu tham gia dự án |
| `git fetch` | Lấy thông tin remote | Đồng bộ metadata |
| `git pull` | Fetch và tích hợp code | Cập nhật branch |
| `git switch` | Chuyển branch | Đổi branch |
| `git switch -c` | Tạo branch | Bắt đầu task |
| `git status` | Kiểm tra trạng thái | Thường xuyên |
| `git diff` | Xem thay đổi | Trước commit |
| `git add` | Stage code | Chuẩn bị commit |
| `git commit` | Lưu commit | Hoàn thành thay đổi |
| `git push` | Đẩy branch | Chia sẻ code |
| `git merge` | Tích hợp branch | Đồng bộ code |
| `git rebase` | Di chuyển commit | Làm mới lịch sử |
| `git stash` | Tạm cất thay đổi | Chuyển việc |
| `git log` | Xem lịch sử | Debug |
| `git restore` | Khôi phục file | Cẩn trọng |
| `git cherry-pick` | Lấy commit cụ thể | Trường hợp đặc biệt |

## Lệnh nguy hiểm - cần cẩn trọng

- `git push --force` / `git push --force-with-lease`: Ghi đè lịch sử remote. Cấm sử dụng trừ khi được Tech Lead cho phép.
- `git reset --hard`: Xóa thay đổi local chưa commit. Có thể mất dữ liệu.
- `git branch -D`: Xóa branch local kể cả khi chưa merge.
- `git clean -fd`: Xóa file untracked. Có thể mất file mới.
- `git restore`: Khôi phục file, mất thay đổi chưa commit.

Trước khi chạy các lệnh này, hãy kiểm tra `git status` và hỏi người phụ trách nếu không chắc chắn.
