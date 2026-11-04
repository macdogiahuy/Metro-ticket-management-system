# Hướng dẫn nhanh cho Developer Intern

## Ngày đầu tiên

1. Cài Git.
2. Clone repository:

```bash
git clone https://github.com/macdogiahuy/Metro-ticket-management-system.git
cd Metro-ticket-management-system
```

3. Kiểm tra remote:

```bash
git remote -v
```

4. Fetch và checkout `dev`:

```bash
git fetch origin
git checkout dev
git pull --ff-only origin dev
```

5. Đọc `docs/git-workflow/README.md` và `BRANCH-POLICY.md`.

## Khi nhận task

1. Pull code mới nhất:

```bash
git switch dev
git fetch origin
git pull --ff-only origin dev
```

2. Tạo feature branch:

```bash
git switch -c feature/US-101-user-authentication
```

3. Đọc yêu cầu task.

4. Code và commit:

```bash
git status
git diff
git add .
git commit -m "feat(auth): implement user authentication"
```

## Khi hoàn thành task

1. Fetch và cập nhật code:

```bash
git fetch origin
git switch dev
git pull --ff-only origin dev
git switch feature/US-101-user-authentication
git merge dev
```

2. Xử lý conflict nếu có (xem `CONFLICT-RESOLUTION.md`).

3. Push branch:

```bash
git push -u origin feature/US-101-user-authentication
```

4. Tạo Pull Request target `dev`.

5. Chờ review và sửa feedback.

6. Kiểm tra PR đã merge.

7. Sau merge, đồng bộ và xóa branch local:

```bash
git fetch origin
git switch dev
git pull --ff-only origin dev
git branch -d feature/US-101-user-authentication
```

## Khi gặp lỗi

1. Xem `git status`.
2. Xem `git log`.
3. Xem `git diff`.
4. Xác định branch hiện tại.
5. Không chạy các lệnh phá hủy dữ liệu nếu chưa hiểu tác dụng.

## Những lệnh Intern KHÔNG được tự ý sử dụng

```bash
git push origin main
git push origin dev
git push --force
git push --force-with-lease
git reset --hard
git branch -D
git clean -fd
```

Giải thích:

- `git push origin main`: Push trực tiếp lên production branch, bị cấm.
- `git push origin dev`: Push trực tiếp lên integration branch, bị cấm.
- `git push --force`: Ghi đè lịch sử remote, có thể mất code của người khác.
- `git push --force-with-lease`: Cũng là force push, cần cho phép.
- `git reset --hard`: Xóa thay đổi local chưa commit, có thể mất dữ liệu.
- `git branch -D`: Xóa branch local kể cả khi chưa merge.
- `git clean -fd`: Xóa file untracked, có thể mất file mới.

Nếu cần dùng các lệnh này, phải hỏi Tech Lead trước.
