# MyMBG Backend API

## 📋 Deskripsi Proyek

Backend untuk sistem pencatatan dapur MBG - aplikasi manajemen produksi, distribusi, dan keuangan untuk dapur rumah makan. API ini menyediakan endpoint untuk CRUD data, manajemen resep, tracking produksi, dan pelaporan keuangan dengan sistem autentikasi Bearer Token.

## 🚀 Quick Start

### Prasyarat

- .NET 10.0 atau lebih baru
- PostgreSQL 12 atau lebih baru
- Git

### Setup Lokal

1. **Clone repository**

```bash
git clone https://github.com/rockhubzz/MyMBG-backend
cd mymbg_backend
```

2. **Konfigurasi database**

```bash
# Set environment variable untuk connection string
$env:POSTGRES_CONNECTION_STRING = "Host=localhost;Port=5432;Username=postgres;Database=mymbg_db"
```

3. **Restore dependencies dan build**

```bash
cd MyMBG
dotnet restore
dotnet build
```

4. **Jalankan aplikasi**

```bash
dotnet run
```

Server akan berjalan di `http://localhost:5292`

### Testing API

**Login dan dapatkan Bearer Token**

```bash
curl -X POST http://localhost:5292/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","password":"password"}'
```

**Gunakan token untuk akses protected endpoints**

```bash
curl -H "Authorization: Bearer <your_token>" \
  http://localhost:5292/api/crud/produksi
```

## 📦 Teknologi Utama

| Teknologi             | Versi    | Fungsi                          |
| --------------------- | -------- | ------------------------------- |
| .NET                  | 10.0     | Framework utama backend         |
| PostgreSQL            | 12+      | Database relasional             |
| Entity Framework Core | Latest   | ORM untuk database              |
| Npgsql                | Latest   | PostgreSQL data provider        |
| Minimal APIs          | Built-in | Routing dan endpoint management |

## 🏗️ Arsitektur & Pola Desain

### Arsitektur Monolitik dengan Layered Pattern

```
MyMBG/
├── Program.cs           # Konfigurasi DI Container & startup
├── Endpoints/           # Logical layering untuk berbagai domain
│   ├── AuthEndpoints.cs
│   ├── CrudEndpoints.cs
│   └── ProduksiEndpoints.cs
├── Data/                # Data access layer
│   ├── ApplicationDbContext.cs
│   ├── GenericCrudRepository.cs
│   ├── EntityMetadataProvider.cs
│   ├── DatabaseBootstrapper.cs
│   └── TokenValidator.cs
└── Models/              # Domain models & DTOs
    └── CrudModels.cs
```

### Pola Desain yang Diterapkan

1. **Repository Pattern**: Abstraksi akses data melalui `GenericCrudRepository`
2. **Dependency Injection**: DI container untuk loose coupling antar komponen
3. **Generic CRUD**: GenericCrudRepository menangani operasi CRUD generik untuk semua entity
4. **Bearer Token Authentication**: Token-based security dengan validasi server-side
5. **Transaction Management**: Transactional consistency untuk operasi produksi kompleks

### Fitur Utama

- **Multi-entity CRUD**: Support untuk dinamis entity management
- **Entity Metadata**: Sistem untuk mendeskripsikan struktur entity
- **Bearer Token Auth**: Autentikasi berbasis token dengan 7-day expiry
- **Produksi Workflow**: Complex workflow untuk sesi produksi dengan scaling resep
- **Stock Management**: Automatic stock deduction dengan trigger functions

## 🔒 Keamanan

- **Bearer Token**: Semua protected endpoints memerlukan valid Bearer token di header `Authorization: Bearer <token>`
- **Token Storage**: Token disimpan di database dan divalidasi server-side
- **Token Expiry**: Token otomatis kadaluarsa setelah 7 hari
- **Password Hashing**: PBKDF2 dengan 100,000 iterations untuk password hashing

## 🌐 Deployment

### Production API URL

```
http://32.236.125.230:5292/api
```

### Deploy ke Vercel (Contoh)

```bash
# Pastikan account Vercel sudah terbuat
vercel login
vercel --prod
```

## 📡 API Endpoints Utama

| Method | Endpoint                  | Deskripsi                     | Auth |
| ------ | ------------------------- | ----------------------------- | ---- |
| POST   | `/api/auth/register`      | Register user baru            | ✗    |
| POST   | `/api/auth/login`         | Login & dapatkan token        | ✗    |
| GET    | `/api/crud/meta/entities` | List semua entity             | ✓    |
| GET    | `/api/crud/{entity}`      | List entity dengan pagination | ✓    |
| POST   | `/api/crud/{entity}`      | Create entity baru            | ✓    |
| PUT    | `/api/crud/{entity}/{id}` | Update entity                 | ✓    |
| DELETE | `/api/crud/{entity}/{id}` | Delete entity                 | ✓    |
| POST   | `/api/produksi`           | Create sesi produksi          | ✓    |

## 📊 Database Schema

### Tables Utama

- **users**: Pengguna sistem
- **tokens**: Session tokens dengan expiry
- **produksi**: Sesi produksi
- **resep**: Master resep
- **resep_bahan**: Detail bahan per resep
- **bahan_baku**: Master bahan baku
- **distribusi**: Tracking distribusi produk
- **keuangan**: Pencatatan transaksi keuangan
- **sesi_produksi**: Sesi produksi dengan status tracking
- **penggunaan_bahan**: Log penggunaan bahan per sesi

## 🐛 Troubleshooting

### Error 500 pada Login

- Pastikan table `tokens` sudah dibuat (aplikasi auto-create saat startup)
- Verifikasi connection string ke PostgreSQL
- Lihat logs di console untuk detail error

### Unauthorized (401)

- Pastikan Bearer token valid dan belum expired
- Token harus di header dengan format: `Authorization: Bearer <token>`

### Connection Timeout

- Verifikasi PostgreSQL server running
- Check connection string (host, port, credentials)
