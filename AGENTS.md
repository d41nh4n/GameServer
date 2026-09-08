# Quy tắc làm việc tiết kiệm quota

- Trả lời bằng tiếng Việt, ngắn gọn. Không in lại toàn bộ file đã sửa.
- Mỗi yêu cầu chỉ thực hiện một chức năng hoặc một lỗi cụ thể.
  Không tự mở rộng phạm vi, refactor hoặc thêm dependency không cần thiết.
- Trước khi sửa, tìm bằng rg và chỉ đọc những file, đoạn code liên quan.
  Không đọc toàn bộ repository, node_modules, bin, obj hoặc log lớn.
- Giới hạn output của lệnh; chỉ lấy phần lỗi và ngữ cảnh cần thiết.
- Tái sử dụng thông tin đã kiểm tra trong phiên, trừ khi file đã thay đổi.
- Không tự tạo subagent, chạy nhiều model, hoặc review lặp lại.
- Chỉ tìm web khi cần xác minh API, phiên bản hoặc thông tin còn thiếu.
- Chạy build/test phù hợp với phần thay đổi. Giữ các kiểm tra bắt buộc;
  không bỏ kiểm tra an toàn để tiết kiệm quota.
- Khi một cách sửa thất bại hai lần mà không có bằng chứng mới,
  dừng thử lặp lại; báo nguyên nhân đã biết và dữ liệu cần bổ sung.
- Hoàn thành phạm vi được giao rồi dừng. Báo ngắn:
  thay đổi chính, kiểm tra đã chạy, vấn đề còn lại.
- Khi được yêu cầu kết thúc phiên, cập nhật docs/progress.md ngắn gọn:
  trạng thái, quyết định, file liên quan, lỗi còn lại và bước tiếp theo.
- Không tự chuyển sang model đắt hơn hoặc provider API trả phí.
- Không sửa world thật, restart game đang chạy, đổi firewall hoặc
  chạy lệnh quản trị nếu chưa được yêu cầu rõ ràng.
