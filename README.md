# OrderService - E-Commerce Order Management Microservice

## 📌 Proje Özeti
OrderService, modern e-ticaret sistemleri için **sipariş yönetimi** işlevlerini yerine getiren bir mikroservistir.  
**Gerçek zamanlı stok rezervasyonu**, **idempotent sipariş oluşturma**, **payment ve cancel akışları**, **event-driven** mimari ve **stok kuralları** ile tasarlanmıştır.

## 🏗 Mimari
- **Backend**: .NET 9, ASP.NET Core Web API
- **Mesajlaşma**: RabbitMQ (fanout + direct exchange + DLX)
- **Veritabanı**: PostgreSQL + EF Core (Code First + Migrations)
- **Cache / Queue / Event**: RabbitMQ & InMemory Store
- **Pattern’ler**:
  - CQRS + MediatR
  - Event-Driven Architecture
  - Idempotency Pattern
  - Domain-Driven Design (Entities, Value Objects)
  - Optimistic Locking (`RowVersion`)

### Servisler
- **Order API** → Sipariş CRUD, idempotent create
- **Inventory API** → Stok kontrol, rezervasyon, iade
- **Consumers**:
  - `InventoryReservationConsumer` → `order.created` event’ini dinleyip stok rezervasyonu yapar
  - `PaymentProcessConsumer` → Payment işlemlerini simüle eder
  - `OrderStatusUpdaterConsumer` → Payment sonuçlarına göre order status günceller

---

## ⚙ Kullanılan Teknolojiler
- **.NET 9**
- **Entity Framework Core** + **Npgsql**
- **RabbitMQ**
- **xUnit** + **FluentAssertions**
- **MediatR**
- **Docker Compose** (opsiyonel local RabbitMQ çalıştırma)

---

## 🚀 Kurulum ve Çalıştırma

### 1. Repository’yi klonla
```bash
git clone https://github.com/yourname/orderservice.git
cd orderservice
```

### 2. PostgreSQL ve RabbitMQ’yu başlat
```bash
docker compose up -d rabbitmq postgres
```
- RabbitMQ Management UI: [http://localhost:15672](http://localhost:15672) (guest/guest)

### 3. AppSettings’i kontrol et
`appsettings.json` içinde DB ve RabbitMQ ayarlarını yap.

### 4. Migration uygula
```bash
dotnet ef migrations add Init --project src/OrderService.Infrastructure
dotnet ef database update --project src/OrderService.Infrastructure
```

### 5. Uygulamayı başlat
```bash
dotnet run --project src/OrderService.Api/OrderService.Api.csproj --urls http://localhost:8080
```

---

## 🗄 Migration / Database Notları
- EF Core ile Code-First yaklaşımı
- **`idempotency`** tablosu, **unique index (`uq_idempotency_key`)** ile korunur
- Migration sonrası tablo ve index otomatik oluşur
- Idempotency key çakışmalarında eski order döner, stok düşmez

---
## ➡︎ Özellikler

- **Event-Driven Architecture:** RabbitMQ üzerinden event publish/subscribe.
- **Idempotency:** Aynı isteğin tekrarlandığında yan etkisiz olması.
- **Optimistic Locking:** RowVersion ile concurrency kontrolü.
- **Stok Kuralları:**
    - Tek siparişte mevcut stoğun %50’sinden fazlası rezerve edilemez.
    - Flash sale ürünlerde müşteri başına max 2 adet.
    - Low stock uyarısı (stok < 10).

---

## 🔄 Akışlar

### Order Creation
1. `POST /api/v1/orders` → CreateOrderCommandHandler çalışır
2. Idempotency kontrolü yapılır
3. Order DB’ye eklenir
4. `order.created` event publish edilir
5. Inventory rezervasyon yapılır

### Payment
- `payment.processed` → Order status `Confirmed`
- `payment.failed` → Order status `Cancelled` + stok iade

### Inventory Kuralları
- %50 Rule: Tek sipariş mevcut stoğun %50’sinden fazlasını rezerve edemez
- Flash Sale Rule: Belirlenen ürünlerde müşteri başına max 2 adet
- Low Stock Alert: <10 stok kaldığında log uyarısı
- TTL Rule: Rezervasyon süresi dolarsa otomatik iade

---

## 🧪 Test Edilen Özel Durumlar

1. **Başarılı Sipariş Oluşturma**
   - Stok düşer, order created event oluşur

2. **Idempotent Sipariş Oluşturma**
   - Aynı key ile tekrar çağrı → aynı orderId döner, stok düşmez

3. **Stok Yetersizliği**
   - Sipariş toplamı stoktan büyükse rezervasyon yapılmaz

4. **%50 Rule**
   - Tek sipariş mevcut stoğun yarısından fazlasını rezerve edemez

5. **Flash Sale Rule**
   - Müşteri başına max 2 adet

6. **Payment Success / Fail**
   - Success → Confirm
   - Fail → Cancel + stok iade

---

## 🖥 cURL Test Komutları

### 1. Stok Set Etme
```bash
curl -s -X POST http://localhost:8080/api/v1/inventory/admin/bulk-set   -H "Content-Type: application/json"   -d '{"items":{
    "44444444-4444-4444-4444-444444444444":100,
    "55555555-5555-5555-5555-555555555555":12
  }}' | jq
```

### 2. Stok Görüntüleme
```bash
curl -s http://localhost:8080/api/v1/inventory/products/44444444-4444-4444-4444-444444444444/stock
```

### 3. Sipariş Oluşturma
```bash
curl -s -X POST http://localhost:8080/api/v1/orders   -H "Content-Type: application/json"   -d '{
    "idempotencyKey":"idem-001",
    "customerId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "items":[
      {"productId":"44444444-4444-4444-4444-444444444444","quantity":3,"unitPrice":20.00},
      {"productId":"55555555-5555-5555-5555-555555555555","quantity":2,"unitPrice":20.00}
    ]
  }' | jq
```

### 4. Aynı Idempotency ile Sipariş
```bash
curl -s -X POST http://localhost:8080/api/v1/orders   -H "Content-Type: application/json"   -d '{
    "idempotencyKey":"idem-001",
    "customerId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "items":[
      {"productId":"44444444-4444-4444-4444-444444444444","quantity":3,"unitPrice":20.00},
      {"productId":"55555555-5555-5555-5555-555555555555","quantity":2,"unitPrice":20.00}
    ]
  }' | jq
```

### 5. Order Detay
```bash
OID=<orderId>
curl -s http://localhost:8080/api/v1/orders/$OID | jq
```

---

## 📌 Ek Notlar
- **Idempotency** uygulandı → stok ve order consistency sağlandı
- **Optimistic Locking** → `RowVersion` ile race condition önlendi
- **DLQ (Dead Letter Queue)** kullanıldı → hatalı mesajlar kaybolmaz
- **Testler**: xUnit + FluentAssertions ile unit testleri yazıldı

---
