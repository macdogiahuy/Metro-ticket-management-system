# Chính sách Branch của dự án

## Branch strategy

- `main`: Production code, ổn định, đã kiểm thử.
- `dev`: Development integration.
- Feature branches: `feature/*`, `bugfix/*`, `hotfix/*`, `chore/*`, `refactor/*`, `docs/*`.

Luồng phát triển:

```text
feature/* -> PR -> dev -> PR -> main
```

Hotfix:

```text
hotfix/* -> PR -> main
```

Sau khi hotfix merge vào `main`, phải đồng bộ về `dev` thông qua PR.

## Naming convention

- `feature/US-XXX-feature-name`
- `bugfix/US-XXX-bug-name`
- `hotfix/US-XXX-critical-fix`
- `release/v1.0.0`
- `chore/US-XXX-task-name`
- `refactor/US-XXX-refactor-name`
- `docs/US-XXX-documentation`

## Permission policy

- Intern: Push feature branch, tạo PR.
- Developer: Push feature branch, tạo PR, review.
- Reviewer: Review và approve.
- Tech Lead: Approve, quản lý merge.
- Maintainer: Quản lý repository và branch rules.
- Admin: Quản trị repository.

Không cấp quyền bypass branch protection cho Intern/Developer.

## PR policy

- Mọi thay đổi phải đi qua Pull Request.
- PR feature/* target `dev`, không target `main`.
- PR phải có mô tả rõ ràng, link task/issue.
- PR phải pass CI checks trước khi merge.

## Approval policy

- `main`: ít nhất 2 approvals.
- `dev`: ít nhất 1 approval.
- Dismiss stale approvals khi có push mới.
- Yêu cầu resolve conversations trước khi merge.

## Merge strategy

- Squash and merge hoặc merge commit tùy team thống nhất.
- Không force merge.

## Conflict policy

- Tự xử lý conflict theo `CONFLICT-RESOLUTION.md`.
- Nếu conflict liên quan business logic, DB schema, API contract, auth: hỏi người phụ trách.
- Sau resolve phải chạy test.

## Hotfix policy

- Tạo branch `hotfix/*` từ `main`.
- PR target `main`.
- Sau merge, đồng bộ `main` -> `dev` qua PR.

## Release policy

- Chỉ tạo release branch khi cần quản lý phiên bản riêng.
- Format: `release/v1.0.0`.

## Hành động bị cấm

- Push trực tiếp lên `main` hoặc `dev`.
- Force push.
- Xóa branch protected.
- Bypass branch protection.
- Merge PR của chính mình khi chưa được phê duyệt.
- Bỏ qua CI checks.
- Commit secrets hoặc dữ liệu nhạy cảm.
