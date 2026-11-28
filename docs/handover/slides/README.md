# DOC-01 — Slide 10 phút & video dự phòng

## Trình chiếu

Mở `slides/index.html` trong trình duyệt (Chrome/Edge), sau đó:

- `→` / `Space` / click trang: slide tiếp theo; `←`: slide trước.
- `N`: hiện/ẩn **ghi chú thuyết trình** (có sẵn trên từng slide).
- Thanh tiến độ và số slide hiển thị ở dưới cùng.

Deck gồm **10 slide / ~10 phút**: tiêu đề → phạm vi → kiến trúc → demo 1/2/3
→ hardening → bằng chứng → bàn giao → Q&A. Nội dung bám SRS v1.2 / UC v1.6 và
số liệu thật từ lượt kiểm chứng 28/11/2026. Biểu đồ dùng Chart.js khi có mạng;
khi offline tự xuống thanh bar CSS tương đương.

## Tài sản

| Đường dẫn | Mô tả |
| --- | --- |
| `slides/index.html` | Deck HTML, không cần build, chạy offline. |
| `slides/captures/slide-01.png … slide-10.png` | Ảnh chụp 1280×720 từng slide (bằng chứng + nguồn dựng video). |
| `video/metro-demo-fallback.mp4` | Video dự phòng ~9,9 phút, 1280×720, 610 KB, không tiếng. |

**Video dự phòng là slide-based**, không phải bản ghi màn hình ứng dụng: dùng khi
live demo không khả dụng, phát song song lời thuyết trình trực tiếp. Khi FE-01→FE-09
và QA-03 hoàn tất, nên thay bằng bản quay màn hình thật theo `DEMO_SCRIPT.md`.

## Tái tạo ảnh & video

```bash
# 1) Capture 10 slide bằng Chrome headless (từ thư mục repo)
for i in 1 2 3 4 5 6 7 8 9 10; do
  chrome --headless=new --disable-gpu --hide-scrollbars \
    --window-size=1280,720 --virtual-time-budget=2500 \
    --screenshot="docs/handover/slides/captures/slide-$(printf %02d $i).png" \
    "file:///$PWD/docs/handover/slides/index.html?slide=$i"
done

# 2) Ghép thành MP4 (concat.txt đã có trong thư mục captures)
ffmpeg -f concat -safe 0 -i docs/handover/slides/captures/concat.txt \
  -vf "fps=1,format=yuv420p" -c:v libx264 -preset veryfast -crf 28 \
  -movflags +faststart docs/handover/video/metro-demo-fallback.mp4
```

Trên Windows thay `chrome` bằng đường dẫn đầy đủ tới `chrome.exe`. Các lệnh trên
chỉ tạo lại tài sản có trong repo; không cần secret hay kết nối production.
