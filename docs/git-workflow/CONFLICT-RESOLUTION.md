# Xử lý Merge Conflict an toàn

## Merge conflict là gì?

Merge conflict xảy ra khi Git không thể tự động gộp thay đổi từ hai branch vì cùng một phần code bị chỉnh sửa theo hai cách khác nhau.

## Nguyên nhân thường gặp

- Hai người cùng sửa một file, cùng một dòng.
- Một branch xóa file/code mà branch khác sửa.
- Thay đổi chồng chéo trong cùng một hàm hoặc cùng một block code.
- Đồng bộ code không thường xuyên, để branch quá cũ.

## Cách nhận biết conflict markers

```text
<<<<<<< HEAD
const timeout = 3000;
=======
const timeout = 5000;
>>>>>>> dev
```

Giải thích:

- `<<<<<<< HEAD`: phần code từ branch hiện tại (branch đang checkout).
- `=======`: dấu phân cách giữa hai phía.
- `>>>>>>> dev`: phần code từ branch đang merge vào.

## Quy trình xử lý

### Với merge

1. Kiểm tra branch hiện tại:

```bash
git status
```

2. Xem các file đang conflict.

3. Đọc và so sánh thay đổi từ hai phía.

4. Xác định logic nghiệp vụ đúng.

5. Chỉnh sửa code thủ công, xóa conflict markers.

6. Kiểm tra diff:

```bash
git diff
```

7. Chạy formatter, lint, build và test phù hợp.

8. Stage các file đã xử lý:

```bash
git add <RESOLVED_FILES>
```

9. Tiếp tục merge:

```bash
git commit
```

### Với rebase

1. Kiểm tra branch hiện tại:

```bash
git status
```

2. Xử lý conflict trong từng commit.

3. Sau khi xử lý:

```bash
git add <RESOLVED_FILES>
git rebase --continue
```

## Cách hủy nếu không thể xử lý

Với merge:

```bash
git merge --abort
```

Với rebase:

```bash
git rebase --abort
```

## Quy tắc tuyệt đối khi xử lý conflict

- Không tự động chọn Accept Current hoặc Accept Incoming khi chưa hiểu nội dung.
- Không sử dụng `git checkout --ours` hoặc `--theirs` trên toàn bộ repository một cách máy móc.
- Không xóa code chỉ để conflict biến mất.
- Không force push để giải quyết conflict.
- Nếu conflict liên quan đến business logic, database schema, API contract hoặc authentication, phải hỏi người phụ trách.
- Sau khi resolve phải chạy test và kiểm tra diff.
- Nếu không chắc chắn, dừng lại và nhờ reviewer hỗ trợ.

## Sử dụng VSCode Merge Editor

VSCode cung cấp Merge Editor giúp so sánh và chọn thay đổi từ hai phía. Tuy nhiên, intern vẫn phải hiểu nội dung trước khi chọn. Không bấm Accept Incoming/Current một cách máy móc.

## Kiểm thử sau khi resolve

- Chạy build.
- Chạy unit tests.
- Kiểm tra diff cuối cùng.
- Đảm bảo không còn conflict markers trong code.
