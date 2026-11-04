# Git Workflow của dự án Metro Ticket Management System

Tài liệu này dành cho Developer, đặc biệt là Developer Intern/Fresher, để hiểu và làm việc đúng quy trình Git của dự án.

## 1. Tổng quan

Dự án sử dụng mô hình Git Workflow với hai branch chính:

- `main`: Code production, ổn định, đã kiểm thử.
- `dev`: Code tích hợp từ các feature, dùng để phát triển và kiểm thử tích hợp.

Luồng phát triển chuẩn:

```text
feature/* -> Pull Request -> dev -> Pull Request -> main
```

Hotfix khẩn cấp:

```text
hotfix/* -> Pull Request -> main
```

Sau khi hotfix được merge vào `main`, phải tạo Pull Request đồng bộ thay đổi từ `main` về `dev` để tránh lệch code.

## 2. Mô tả các branch

### main

- Chứa code ổn định, đã được kiểm thử.
- Chỉ nhận code thông qua Pull Request.
- Không cho phép push trực tiếp.
- Không cho phép force push.
- Không cho phép xóa branch.
- Yêu cầu ít nhất 2 approvals.
- Bắt buộc CI checks thành công trước khi merge.
- Chỉ Tech Lead hoặc Maintainer được phép merge.

### dev

- Chứa code tích hợp từ các feature.
- Chỉ nhận code thông qua Pull Request.
- Không cho phép push trực tiếp.
- Không cho phép force push.
- Không cho phép xóa branch.
- Yêu cầu ít nhất 1 approval.
- Bắt buộc CI checks thành công trước khi merge.
- Developer không được tự ý merge PR của chính mình.

### Branch làm việc

Không tạo sẵn hàng loạt branch feature rỗng. Chỉ tạo khi có nhiệm vụ cụ thể.

Quy ước tên:

- `feature/US-XXX-feature-name`
- `bugfix/US-XXX-bug-name`
- `hotfix/US-XXX-critical-fix`
- `release/v1.0.0`
- `chore/US-XXX-task-name`
- `refactor/US-XXX-refactor-name`
- `docs/US-XXX-documentation`

Ví dụ:

- `feature/US-101-user-authentication`
- `feature/US-102-booking-management`
- `bugfix/US-103-fix-payment-error`
- `hotfix/US-104-fix-production-crash`

## 3. Quy tắc đặt tên commit message

Sử dụng Conventional Commits:

```text
<type>(<scope>): <description>
```

Các type phổ biến:

- `feat`: Thêm tính năng mới.
- `fix`: Sửa lỗi.
- `docs`: Cập nhật tài liệu.
- `style`: Format code, không thay đổi logic.
- `refactor`: Tái cấu trúc code.
- `test`: Thêm hoặc sửa test.
- `chore`: Công việc bảo trì, cấu hình.
- `ci`: Thay đổi CI/CD.

Ví dụ:

```text
feat(auth): implement user login
fix(booking): resolve payment callback error
docs(git): add conflict resolution guide
```

## 4. Giải thích các khái niệm Git cơ bản

### fetch

Lấy thông tin mới nhất từ remote về local, nhưng không thay đổi branch đang làm việc.

### pull

Gồm fetch + merge (hoặc rebase). Cập nhật branch local bằng code từ remote.

### merge

Tích hợp thay đổi từ một branch vào branch hiện tại. Có thể tạo merge commit.

### rebase

Di chuyển các commit của branch hiện tại lên trên commit mới nhất của branch khác, làm lịch sử tuyến tính hơn.

### push

Đẩy các commit local lên remote.

### Pull Request (PR)

Yêu cầu tích hợp code từ branch này sang branch khác, cho phép review, thảo luận và kiểm thử trước khi merge.

## 5. Quy trình nhận task

1. Đọc yêu cầu task hoặc issue.
2. Xác định ID task, ví dụ `US-101`.
3. Pull code mới nhất từ `dev`.
4. Tạo feature branch mới.
5. Code, commit và push.
6. Tạo Pull Request target `dev`.
7. Chờ review và xử lý feedback.
8. Sau khi merge, xóa branch local.

## 6-17. Chi tiết

Xem các file sau để biết chi tiết từng bước:

- `DAILY-WORKFLOW.md`: Quy trình làm việc hằng ngày.
- `CONFLICT-RESOLUTION.md`: Xử lý merge conflict.
- `GIT-COMMANDS.md`: Tra cứu nhanh lệnh Git.
- `BRANCH-POLICY.md`: Chính sách branch chính thức.
- `INTERN-QUICK-START.md`: Hướng dẫn nhanh cho Intern.

## 18. Những thao tác Git bị cấm

- Push trực tiếp lên `main` hoặc `dev`.
- Force push (`git push --force`, `git push --force-with-lease`).
- Reset hard (`git reset --hard`) khi chưa hiểu rõ.
- Xóa branch (`git branch -D`) mà không có lý do rõ ràng.
- Chạy `git clean -fd` mà không kiểm tra trước.
- Merge PR của chính mình khi chưa được phê duyệt.
- Bỏ qua CI checks.

Nếu không chắc chắn về một lệnh Git, hãy hỏi Tech Lead hoặc Reviewer trước khi thực hiện.
