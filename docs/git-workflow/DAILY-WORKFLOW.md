# Quy trình làm việc hằng ngày với Git

## A. Lần đầu tham gia dự án

```bash
git clone https://github.com/macdogiahuy/Metro-ticket-management-system.git
cd Metro-ticket-management-system

git fetch origin
git checkout dev
git pull --ff-only origin dev
```

## B. Bắt đầu task mới

```bash
git switch dev
git fetch origin
git pull --ff-only origin dev

git switch -c feature/US-101-user-authentication
```

## C. Trong quá trình làm việc

```bash
git status
git diff
git add .
git commit -m "feat(auth): implement user authentication"
```

Khuyến khích chia commit thành các thay đổi nhỏ, có ý nghĩa.

## D. Cập nhật code mới từ dev

Trường hợp branch chưa có commit riêng:

```bash
git fetch origin
git switch dev
git pull --ff-only origin dev
```

Trường hợp đang làm việc trên feature branch:

```bash
git fetch origin
git switch dev
git pull --ff-only origin dev

git switch feature/US-101-user-authentication
git merge dev
```

Hoặc sử dụng rebase khi phù hợp:

```bash
git fetch origin
git rebase origin/dev
```

Giải thích:

- Dùng `merge` khi muốn giữ lịch sử rõ ràng và an toàn, đặc biệt cho Intern.
- Dùng `rebase` khi muốn lịch sử tuyến tính và branch chưa được chia sẻ cho người khác.
- Không khuyến khích Intern tự rebase branch đã được nhiều người cùng sử dụng hoặc đã được chia sẻ nếu chưa thống nhất với team.

## E. Push branch

```bash
git push -u origin feature/US-101-user-authentication
```

Nếu branch đã tồn tại trên remote:

```bash
git push
```

## F. Tạo Pull Request

Hướng dẫn:

- Source branch: `feature/US-101-user-authentication`
- Target branch: `dev`
- PR title: rõ ràng, theo commit convention.
- PR description: mô tả thay đổi, link task/issue.
- Checklist đầy đủ.
- Assign reviewer phù hợp.
- Xử lý feedback.
- Chờ approval.
- Merge sau khi đủ điều kiện.

Mặc định PR từ `feature/*` phải target `dev`, không target `main`.

## G. Sau khi merge

```bash
git fetch origin
git switch dev
git pull --ff-only origin dev
```

Sau đó xóa branch local nếu task đã hoàn thành:

```bash
git branch -d feature/US-101-user-authentication
```

Remote branch chỉ xóa sau khi PR đã merge và repository cho phép xóa branch.
