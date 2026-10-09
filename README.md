# SmartWare AI – Hệ thống quản lý kho tích hợp Trợ lý AI

SmartWare AI là ứng dụng web quản lý kho hàng xây dựng bằng **ASP.NET Core MVC (.NET 10)** và
**SQL Server**, tích hợp **Trợ lý AI** dùng Google Gemini. Hệ thống quản lý danh mục hàng hoá, nhà
cung cấp, khách hàng, nhập kho, xuất kho, tồn kho, đơn hàng và báo cáo. Người dùng cũng có thể hỏi
bằng tiếng Việt tự nhiên để tra cứu dữ liệu, xem quy trình nội bộ hoặc soạn nhanh phiếu nhập/xuất.

## Mục lục

1. [Chức năng chính](#1-chức-năng-chính)
2. [Trợ lý AI](#2-trợ-lý-ai)
3. [Công nghệ và kiến trúc](#3-công-nghệ-và-kiến-trúc)
4. [Cài đặt và chạy](#4-cài-đặt-và-chạy)
5. [Tài khoản và phân quyền](#5-tài-khoản-và-phân-quyền)
6. [Dữ liệu mẫu](#6-dữ-liệu-mẫu)
7. [Kiểm thử](#7-kiểm-thử)
8. [Cấu trúc thư mục](#8-cấu-trúc-thư-mục)
9. [Tài liệu kèm theo](#9-tài-liệu-kèm-theo)

## 1. Chức năng chính

| Phân hệ | Mô tả |
|---|---|
| Tổng quan (Dashboard) | Chỉ số tồn kho, cảnh báo sắp hết hàng, phiếu gần đây, biểu đồ nhập–xuất 6 tháng. |
| Danh mục, nhà cung cấp, sản phẩm, khách hàng | Thêm/sửa/ngừng sử dụng, tìm kiếm, lọc, phân trang, xuất Excel (CSV), tải ảnh sản phẩm (JPEG/PNG/WebP, tối đa 5 MB, kiểm tra chữ ký file). |
| Nhập kho | Tạo phiếu → Quản lý duyệt/từ chối → hoàn tất: cộng tồn, tính **giá vốn bình quân gia quyền**, ghi giao dịch `IN`. |
| Xuất kho | Tạo phiếu (có thể gắn đơn hàng) → duyệt và **giữ hàng** → hoàn tất: trừ tồn, chốt giá vốn, ghi giao dịch `OUT`. Quản lý có thể **huỷ phiếu** để trả lượng đang giữ về tồn khả dụng. |
| Tồn kho | Tồn thực tế / đã giữ / khả dụng, trạng thái (hết, thấp, bình thường, dư), hàng tồn lâu, lịch sử giao dịch. |
| Đơn hàng bán | Chuyển trạng thái có kiểm soát: chờ xử lý → đang xử lý → đang giao → hoàn tất / giao thất bại / trả hàng. |
| Báo cáo | Nhập, xuất, doanh thu, giá vốn hàng bán (COGS), lợi nhuận gộp, phân tích theo sản phẩm/NCC/khách hàng, **dự báo nhu cầu SMA** và gợi ý nhập hàng; xuất CSV hoặc in. |
| Kho tri thức | Nạp quy trình, chính sách (văn bản, `.txt`, `.md`, `.csv`), phân quyền xem theo vai trò, lập chỉ mục cho tìm kiếm ngữ nghĩa. |
| Người dùng | Quản trị tài khoản và vai trò; mọi thao tác quan trọng đều được ghi **nhật ký kiểm toán**. |

Tiện ích giao diện: tìm nhanh **Ctrl+K** (chuyển trang, thao tác nhanh, tìm sản phẩm/phiếu, hỏi AI),
thu gọn thanh menu, thông báo dạng toast, chống bấm gửi biểu mẫu hai lần, giao diện co giãn cho
điện thoại.

**An toàn dữ liệu khi nhiều người thao tác:** duyệt, hoàn tất và huỷ phiếu chạy trong transaction mức
`Serializable` và kiểm tra `RowVersion`. Nếu dữ liệu vừa bị người khác thay đổi, thao tác bị từ chối
và không có thay đổi nào được ghi.

## 2. Trợ lý AI

Nút **AI Kho** ở góc màn hình mở khung chat. Trợ lý dùng **Gemini function calling**: mô hình không truy
cập cơ sở dữ liệu mà chỉ được chọn trong **12 công cụ** do hệ thống định nghĩa. Backend kiểm tra quyền
rồi mới chạy truy vấn.

| Nhóm việc | Ví dụ câu hỏi | Vai trò |
|---|---|---|
| Tra cứu tồn kho, hàng sắp hết | "SP001 còn bao nhiêu?", "Hàng nào sắp hết?" | Tất cả |
| Phân tích nhập – xuất theo kỳ bất kỳ | "Tháng 9 xuất bao nhiêu ổ SSD?" | Tất cả |
| Theo dõi phiếu | "Phiếu nào đang chờ duyệt?" | Tất cả |
| Quy trình, chính sách (RAG) | "Quy trình duyệt phiếu xuất gồm những bước nào?" | Tất cả (theo quyền tài liệu) |
| Báo cáo, so sánh kỳ, gợi ý nhập hàng | "So sánh doanh thu tháng 9 với tháng 8" | Admin, Manager |
| Đơn hàng, khách hàng | "Đơn của Công ty An Gia đang ở trạng thái nào?" | Admin |
| **Soạn phiếu nhập/xuất** | "Tạo phiếu nhập 20 RAM DDR5 và 10 ổ SSD" | Admin, Employee |

Đặc điểm:

- **Hiểu ngữ cảnh**: hiểu câu hỏi nối tiếp ("nhà cung cấp của *nó* là ai?") và mốc thời gian tương đối
  ("tháng 8", "quý trước"); một câu hỏi có thể dùng nhiều công cụ.
- **Hybrid RAG**: tài liệu quy trình được chia đoạn, tạo embedding 768 chiều, tìm theo độ tương đồng
  cosine kết hợp từ khoá, lọc theo vai trò trước khi đưa vào ngữ cảnh. Câu trả lời ghi rõ tài liệu nguồn.
- **Soạn phiếu – "AI soạn, người quyết"**: trợ lý chỉ soạn **bản nháp**. Nó tìm đúng sản phẩm (hỏi lại khi
  tên mơ hồ), suy ra nhà cung cấp, lấy giá nhập gần nhất và kiểm tra tồn khả dụng, rồi hiện thẻ xem trước.
  Phiếu chỉ được tạo khi người dùng bấm **Xác nhận**, qua đúng dịch vụ tạo phiếu nên vẫn được kiểm tra
  quyền, kiểm tra dữ liệu, ghi nhật ký và ở trạng thái *Chờ duyệt*. Bản nháp gắn với người dùng, chỉ
  dùng được một lần và hết hạn sau 30 phút. Trợ lý **không** duyệt, hoàn tất, huỷ hay xoá phiếu.
- **Phân quyền nhiều lớp**: mỗi vai trò chỉ được khai báo các công cụ được phép; mọi lời gọi công cụ được
  kiểm tra quyền lại trước khi truy vấn; trường nhạy cảm chỉ trả cho vai trò phù hợp.
- **Độ bền**: tự thử lại khi Gemini quá tải tạm thời (429/503); tự chuyển sang model dự phòng khi model
  chính hết hạn mức; khi không gọi được Gemini vẫn trả lời bằng dữ liệu tra cứu trực tiếp.
- **Lịch sử trò chuyện** lưu riêng theo người dùng và vai trò; giới hạn 10 câu hỏi/phút/người dùng.

> Gói miễn phí của Gemini giới hạn số request mỗi ngày cho từng model, mà mỗi câu hỏi dùng 2–3 request.
> Khi trình diễn nên hỏi cách nhau vài giây.

## 3. Công nghệ và kiến trúc

| Thành phần | Công nghệ |
|---|---|
| Web | ASP.NET Core MVC (.NET 10), Razor Views, Bootstrap |
| Cơ sở dữ liệu | SQL Server (LocalDB khi phát triển), Entity Framework Core 10 – Code First, Migrations |
| Xác thực, phân quyền | ASP.NET Core Identity, cookie, Authorization Policies |
| AI | Google Gemini (`gemini-3.6-flash`, function calling), `gemini-embedding-2` cho RAG |
| Kiểm thử | xUnit: unit test, authorization test, integration test trên SQL Server LocalDB |

Mã nguồn tổ chức theo **Clean Architecture**:

```text
SmartWare.Web             Controller, View, ViewModel, JavaScript/CSS
   │  gọi dịch vụ qua interface
SmartWare.Application     Interface dịch vụ, DTO, Command/Query
   ▲  được cài đặt bởi
SmartWare.Infrastructure  EF Core, Identity, dịch vụ nghiệp vụ, Gemini, RAG, công cụ của trợ lý
   │  dùng entity và quy tắc nghiệp vụ
SmartWare.Domain          Entity, Enum, quy tắc nghiệp vụ thuần (giá vốn bình quân, dự báo SMA, …)
```

## 4. Cài đặt và chạy

### Yêu cầu

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- SQL Server LocalDB (cài kèm Visual Studio hoặc SQL Server Express)
- Khoá API Gemini (lấy miễn phí tại [Google AI Studio](https://aistudio.google.com/apikey)); không bắt
  buộc. Không có khoá thì trợ lý chạy ở chế độ tra cứu cơ bản.

### Các bước

**1. Lấy mã nguồn và khôi phục công cụ**

```powershell
git clone https://github.com/TeddyCharites/SmartWare.git
cd SmartWare
dotnet tool restore
```

**2. Tạo cơ sở dữ liệu** (mặc định `SmartWareDB` trên `(localdb)\MSSQLLocalDB`)

```powershell
dotnet tool run dotnet-ef database update `
  --project src/SmartWare.Infrastructure/SmartWare.Infrastructure.csproj `
  --startup-project src/SmartWare.Web/SmartWare.Web.csproj
```

**3. Khai báo tài khoản quản trị và khoá Gemini bằng User Secrets.** Các giá trị này không được lưu trong
mã nguồn.

```powershell
dotnet user-secrets set "DevelopmentAdmin:Email" "admin@smartware.local" --project src/SmartWare.Web
dotnet user-secrets set "DevelopmentAdmin:Password" "<mật-khẩu-của-bạn>" --project src/SmartWare.Web
dotnet user-secrets set "Gemini:ApiKey" "<khoá-gemini-của-bạn>" --project src/SmartWare.Web
```

Mật khẩu cần tối thiểu 8 ký tự, có chữ hoa, chữ thường và chữ số.

**4. Chạy ứng dụng**

```powershell
dotnet run --project src/SmartWare.Web --launch-profile http
```

Mở trình duyệt tại **http://localhost:5215** và đăng nhập bằng tài khoản ở bước 3. Lần chạy đầu tiên
trong môi trường *Development*, hệ thống tự tạo vai trò, tài khoản quản trị và dữ liệu mẫu.

### Cấu hình Gemini (tuỳ chọn, trong `appsettings.json`)

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `Gemini:Model` | `gemini-3.6-flash` | Model sinh câu trả lời |
| `Gemini:FallbackModels` | `gemini-3.5-flash`, `gemini-3.5-flash-lite` | Model dự phòng khi model chính hết hạn mức |
| `Gemini:EmbeddingModel` / `EmbeddingDimensions` | `gemini-embedding-2` / `768` | Embedding cho RAG |
| `Gemini:RagTopK` / `RagMinimumScore` | `3` / `0.35` | Số đoạn tài liệu lấy và ngưỡng liên quan |
| `Gemini:MaxToolRounds` | `4` | Số vòng gọi công cụ tối đa cho một câu hỏi |
| `Gemini:MaxOutputTokens` / `ThinkingLevel` / `TimeoutSeconds` | `4096` / `MINIMAL` / `45` | Giới hạn độ dài, mức suy luận, thời gian chờ |

Có thể thay khoá API bằng biến môi trường `GEMINI_API_KEY` hoặc `Gemini__ApiKey`.

## 5. Tài khoản và phân quyền

Hệ thống có ba vai trò. Khi chạy lần đầu chỉ có tài khoản **Admin**; tài khoản Manager và Employee được
tạo trong mục **Người dùng**.

| Chức năng | Admin | Manager | Employee |
|---|:---:|:---:|:---:|
| Quản lý người dùng, danh mục, NCC, sản phẩm, khách hàng, đơn hàng | ✔ | | |
| Tạo phiếu nhập/xuất (kể cả soạn bằng Trợ lý AI) | ✔ | | ✔ |
| Duyệt/từ chối phiếu, huỷ phiếu xuất | | ✔ | |
| Hoàn tất phiếu (hàng thực sự vào/ra kho) | ✔ | | ✔ |
| Xem tồn kho, sản phẩm, giao dịch | ✔ | ✔ | ✔ |
| Báo cáo, dự báo; quản lý kho tri thức | ✔ | ✔ | |

Nguyên tắc **tách nhiệm vụ**: người tạo phiếu không tự duyệt phiếu, người duyệt không trực tiếp thao tác
hàng ra/vào kho.

## 6. Dữ liệu mẫu

Ở môi trường *Development*, hệ thống tự tạo dữ liệu mẫu (mã bắt đầu bằng `DEMO-`):

- **31 sản phẩm** thuộc 4 danh mục và 3 nhà cung cấp, đủ các trạng thái tồn: hết hàng, sắp hết, bình
  thường, vượt mức tối đa, tồn lâu.
- 4 khách hàng, 6 tháng lịch sử nhập/xuất/bán hàng hoàn tất, cùng các chứng từ đang chờ xử lý để thử quy
  trình duyệt.
- 4 tài liệu quy trình mẫu cho kho tri thức (embedding được tạo khi đã cấu hình khoá Gemini).

Bước tạo dữ liệu chỉ chạy một lần, không bao giờ chạy ngoài môi trường *Development*, và có thể tắt
bằng `DevelopmentSeedData:Enabled = false`.

## 7. Kiểm thử

```powershell
dotnet build SmartWare.slnx
dotnet test SmartWare.slnx --no-build
```

Bộ kiểm thử gồm **167 test**:

- **Unit test (95):** quy tắc nghiệp vụ, xử lý tiếng Việt, tham số và phân quyền công cụ, vòng lặp trợ lý
  với Gemini giả lập, thử lại và đổi model, bản nháp phiếu.
- **Authorization test (60):** policy và token chống CSRF trên từng action.
- **Integration test (12):** chạy trên SQL Server LocalDB với cơ sở dữ liệu tạm (tự tạo và xoá): luồng
  duyệt → giữ hàng → huỷ phiếu, soạn và xác nhận bản nháp phiếu.

## 8. Cấu trúc thư mục

```text
SmartWare/
├── src/
│   ├── SmartWare.Domain/          Entity, Enum, quy tắc nghiệp vụ
│   ├── SmartWare.Application/     Interface dịch vụ, DTO
│   ├── SmartWare.Infrastructure/  EF Core, Identity, dịch vụ, AI (Gemini, RAG, công cụ trợ lý), Migrations, dữ liệu mẫu
│   └── SmartWare.Web/             Controller, View, wwwroot (CSS/JS/ảnh)
├── tests/
│   ├── SmartWare.UnitTests/
│   ├── SmartWare.AuthorizationTests/
│   └── SmartWare.IntegrationTests/
└── docs/                          Tài liệu phân tích thiết kế và tài liệu Trợ lý AI
```

## 9. Tài liệu kèm theo

- `docs/SMARTWARE_AI_BAO_CAO_PHAN_TICH_THIET_KE_DAY_DU (2).docx`: báo cáo phân tích thiết kế hệ thống.
- `docs/SmartWare_AI_Nang_cap_Chatbot.docx`: tài liệu về Trợ lý AI: cách sử dụng, cấu tạo, chức năng,
  so sánh trước/sau nâng cấp, bảo mật và câu hỏi vấn đáp.
