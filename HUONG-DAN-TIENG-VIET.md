# Codex Account Switcher – Bản tiếng Việt (Windows x64)

Bản tùy chỉnh dựa trên mã nguồn người dùng cung cấp (commit 8e4797e33775f729b740b97e9b066a80a186ddcb, phiên bản 0.2.1, MIT). Không chứa thông tin đăng nhập cá nhân.

## Thay đổi

- Giao diện Windows tiếng Việt, nền tối, cửa sổ mặc định 570 DIP, tự mở rộng theo số cột và giới hạn màn hình. Thanh hạn mức 5 giờ và 7 ngày luôn xuất hiện, cùng phần trăm còn lại, thời điểm reset, lượt reset còn lại và hạn gói.
- Mỗi tài khoản có nút Chuyển đổi riêng. Tài khoản đang được core xác minh có thanh xanh lá; không tô xanh nếu chưa xác minh được. Nút hiện tại không khả dụng.
- Thiếu dữ liệu thì ghi Chưa rõ; không thay bằng số 0. Dữ liệu cũ do lỗi cập nhật có cảnh báo. Số lượt reset lấy từ dữ liệu gốc, không tự suy đoán.
- Giữ logic xác thực, lưu hồ sơ, chuyển tài khoản và cập nhật định kỳ 5 phút của mã nguồn gốc. Nút Làm mới gọi lệnh refresh hiện có. Không thêm tự động luân chuyển tài khoản. Không thay đổi thiết lập auto-reset của bản gốc; hãy kiểm tra thiết lập này nếu chỉ muốn xem usage.
- Giữ hộp xác nhận: chuyển tài khoản có thể đóng cưỡng bức Codex Desktop và gián đoạn tác vụ. Hãy lưu công việc trước.
- Không ghi đè lựa chọn ngôn ngữ trong dữ liệu gốc. Lỗi dịch vụ và thông báo từ core có thể vẫn bằng tiếng Anh hoặc ngôn ngữ gốc.
- Tắt cơ chế cập nhật tự động của bản tùy chỉnh để không bị thay thế bằng EXE tiếng Anh của upstream. Các workflow upstream được lưu lại trong docs/upstream-workflows; workflow hoạt động mới chỉ chạy khi người dùng bấm Run workflow.

## Bản cập nhật đồng hồ và bố cục (VI v2)

- Chép đè ba file trong ZIP bản cập nhật vào thư mục repository hiện tại, giữ nguyên đường dẫn. Không thay thế thư mục `.git` hoặc dữ liệu đăng nhập.
- Màn hình tài khoản có nút 1 / 2 / 3 cạnh Quản lý, thanh công cụ cố định và danh sách cuộn dọc. Mặc định một cột khi khởi động lại; việc đổi cột không thay đổi tài khoản đang dùng.
- Đồng hồ 24 DIP, chữ đậm, dưới avatar, cập nhật mỗi giây theo dữ liệu reset đã có. Ví dụ `3p24s`; thời gian dài tách thành hai dòng.
- Nếu hết hạn mức 7 ngày, hiển thị reset 7 ngày. Nếu 7 ngày còn và hết 5 giờ, hiển thị reset 5 giờ. Nếu cả hai còn, chọn mốc tương lai gần nhất. Màu cam khi hết hạn mức, xanh khi còn, xám nếu thiếu dữ liệu hoặc đang chờ xác nhận.
- Khi bộ đếm về 0, đợi lần đồng bộ kế tiếp; không suy đoán usage đã hồi. Đồng hồ không tự gọi mạng hoặc tự chuyển tài khoản.
- Commit rồi Push origin, sau đó chạy lại workflow Build Vietnamese Windows EXE và tải artifact của lần chạy mới. Giữ EXE cũ làm dự phòng. Bộ kiểm tra Windows mới chưa chạy tại môi trường tạo bản vá này.
- Bản vá chỉ sửa giao diện; không sửa cơ chế lấy usage của các tài khoản chưa đồng bộ.

## Cách 1 – tạo EXE bằng GitHub Actions (không cần cài SDK trên máy)

1. Giải nén ZIP. Tạo một repository GitHub riêng tư của bạn; nếu dùng Git, push toàn bộ thư mục này lên nhánh mặc định `main`. Cần đưa cả thư mục ẩn `.github` lên (không chỉ các file hiển thị). Không đưa thư mục dữ liệu ứng dụng hoặc auth.json lên.
2. Vào Actions → Build Vietnamese Windows EXE → Run workflow.
3. Sau khi workflow chạy thành công, mở lần chạy → Artifacts → Codex-Account-Switcher-VI-windows-x64. Giải nén artifact để lấy EXE và SHA256.
4. Workflow cũng chạy bộ kiểm tra UI với dữ liệu giả lập và self-test của gói portable. Nếu có lỗi, đọc log; không có EXE được xác minh nếu bước kiểm tra thất bại.

## Cách 2 – tạo EXE trên Windows

Cài .NET 10 SDK x64, Swift 6.3.3 Windows x64 (và các thành phần Visual Studio C++/Windows SDK mà Swift yêu cầu), PowerShell 7. Kiểm tra các lệnh `dotnet`, `swift`, `pwsh` trong PATH. Chạy `BUILD-EXE.cmd`. File kết quả nằm trong `windows/artifacts/`.

## Dùng với phiên bản đang cài

Thoát hẳn tool cũ từ khay hệ thống trước khi mở bản VI, vì chúng sử dụng cùng mutex và đường dẫn dữ liệu mặc định `%LOCALAPPDATA%\Codex Account Switcher`. Không xóa dữ liệu hay đăng xuất Codex chỉ để đổi giao diện. Có thể quay lại bản cũ bằng cách thoát bản VI và mở EXE cũ. Chỉ dùng tài khoản bạn được phép sử dụng và tuân thủ hạn mức dịch vụ.

## Tình trạng kiểm tra

Đã kiểm tra tĩnh các thay đổi và giữ nguyên core Swift; bổ sung các assertion giao diện cho hiển thị hai hạn mức, trạng thái đã xác minh và dữ liệu không có. Chưa biên dịch hoặc chạy Windows WPF / Swift Windows trong môi trường Linux này; chưa có EXE thực tế hoặc kết quả chạy trên tài khoản thật. Không có chữ ký số riêng cho bản tùy chỉnh.
