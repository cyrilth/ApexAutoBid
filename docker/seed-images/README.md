# Seed images — MinIO `auction-images` bucket

The `minio-init` service in [`../docker-compose.infra.yml`](../docker-compose.infra.yml)
uploads every image file in this directory into the MinIO `auction-images`
bucket on startup (`mc mirror`, excluding this README and `.gitkeep`).

The Auction Service seed data (`DbInitializer`) references these **10 keys** — drop
a royalty-free JPEG with each exact filename here so the seeded auctions render:

| #  | Car            | Filename              |
|----|----------------|-----------------------|
| 1  | Ford GT        | `ford-gt.jpg`         |
| 2  | Bugatti Veyron | `bugatti-veyron.jpg`  |
| 3  | Ford Mustang   | `ford-mustang.jpg`    |
| 4  | Mercedes SLK   | `mercedes-slk.jpg`    |
| 5  | BMW X1         | `bmw-x1.jpg`          |
| 6  | Ferrari Spider | `ferrari-spider.jpg`  |
| 7  | Ferrari F-430  | `ferrari-f430.jpg`    |
| 8  | Audi R8        | `audi-r8.jpg`         |
| 9  | Audi TT        | `audi-tt.jpg`         |
| 10 | Ford Model T   | `ford-model-t.jpg`    |

Each is served publicly at `http://localhost:9000/auction-images/<filename>`
(the bucket has an anonymous **download** policy). After adding or replacing
images, re-run just the init container:

```bash
docker compose -f docker/docker-compose.infra.yml up minio-init
```
