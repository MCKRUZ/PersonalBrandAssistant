diff --git a/.env.example b/.env.example
new file mode 100644
index 0000000..4726745
--- /dev/null
+++ b/.env.example
@@ -0,0 +1,10 @@
+# PostgreSQL Configuration
+POSTGRES_USER=pba
+POSTGRES_PASSWORD=<your-secure-password>
+POSTGRES_DB=personal_brand_assistant
+
+# API Configuration
+API_KEY=<your-api-key>
+
+# Environment (Development or Production)
+ASPNETCORE_ENVIRONMENT=Development
diff --git a/docker-compose.override.yml b/docker-compose.override.yml
new file mode 100644
index 0000000..146c294
--- /dev/null
+++ b/docker-compose.override.yml
@@ -0,0 +1,20 @@
+services:
+  api:
+    build:
+      context: .
+      dockerfile: src/PersonalBrandAssistant.Api/Dockerfile
+    volumes:
+      - ./src:/src
+    environment:
+      ASPNETCORE_ENVIRONMENT: Development
+    command: ["dotnet", "watch", "run", "--project", "/src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj", "--urls", "http://+:8080"]
+
+  web:
+    build:
+      context: src/PersonalBrandAssistant.Web
+      dockerfile: Dockerfile.dev
+    volumes:
+      - ./src/PersonalBrandAssistant.Web:/app
+      - /app/node_modules
+    ports:
+      - "4200:4200"
diff --git a/docker-compose.yml b/docker-compose.yml
new file mode 100644
index 0000000..2219d95
--- /dev/null
+++ b/docker-compose.yml
@@ -0,0 +1,54 @@
+services:
+  db:
+    image: postgres:17-alpine
+    container_name: pba-db
+    environment:
+      POSTGRES_USER: ${POSTGRES_USER}
+      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
+      POSTGRES_DB: ${POSTGRES_DB}
+    ports:
+      - "5432:5432"
+    volumes:
+      - pgdata:/var/lib/postgresql/data
+    healthcheck:
+      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER}"]
+      interval: 5s
+      timeout: 5s
+      retries: 5
+    restart: unless-stopped
+
+  api:
+    build:
+      context: .
+      dockerfile: src/PersonalBrandAssistant.Api/Dockerfile
+    container_name: pba-api
+    ports:
+      - "5000:8080"
+    depends_on:
+      db:
+        condition: service_healthy
+    environment:
+      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
+      ApiKey: ${API_KEY}
+      DataProtection__KeyPath: /data-protection-keys
+      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
+    volumes:
+      - dpkeys:/data-protection-keys
+      - logs:/app/logs
+    restart: unless-stopped
+
+  web:
+    build:
+      context: src/PersonalBrandAssistant.Web
+      dockerfile: Dockerfile
+    container_name: pba-web
+    ports:
+      - "4200:80"
+    depends_on:
+      - api
+    restart: unless-stopped
+
+volumes:
+  pgdata:
+  dpkeys:
+  logs:
diff --git a/src/PersonalBrandAssistant.Api/Dockerfile b/src/PersonalBrandAssistant.Api/Dockerfile
new file mode 100644
index 0000000..82cc4e2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Dockerfile
@@ -0,0 +1,25 @@
+FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
+WORKDIR /src
+
+COPY Directory.Build.props .
+COPY src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj src/PersonalBrandAssistant.Domain/
+COPY src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj src/PersonalBrandAssistant.Application/
+COPY src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj src/PersonalBrandAssistant.Infrastructure/
+COPY src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj src/PersonalBrandAssistant.Api/
+
+RUN dotnet restore src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
+
+COPY src/ src/
+RUN dotnet publish src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj -c Release -o /app/publish --no-restore
+
+FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
+WORKDIR /app
+
+COPY --from=build /app/publish .
+
+RUN mkdir -p /data-protection-keys
+
+ENV ASPNETCORE_URLS=http://+:8080
+EXPOSE 8080
+
+ENTRYPOINT ["dotnet", "PersonalBrandAssistant.Api.dll"]
diff --git a/src/PersonalBrandAssistant.Web/Dockerfile b/src/PersonalBrandAssistant.Web/Dockerfile
new file mode 100644
index 0000000..cecc746
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/Dockerfile
@@ -0,0 +1,16 @@
+FROM node:22-alpine AS build
+WORKDIR /app
+
+COPY package.json package-lock.json ./
+RUN npm ci
+
+COPY . .
+RUN npx ng build --configuration production
+
+FROM nginx:alpine AS runtime
+
+RUN rm /etc/nginx/conf.d/default.conf
+COPY nginx.conf /etc/nginx/nginx.conf
+COPY --from=build /app/dist/personal-brand-assistant/browser/ /usr/share/nginx/html/
+
+EXPOSE 80
diff --git a/src/PersonalBrandAssistant.Web/Dockerfile.dev b/src/PersonalBrandAssistant.Web/Dockerfile.dev
new file mode 100644
index 0000000..5ad0d5c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/Dockerfile.dev
@@ -0,0 +1,9 @@
+FROM node:22-alpine
+WORKDIR /app
+
+COPY package.json package-lock.json ./
+RUN npm install
+
+EXPOSE 4200
+
+CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--poll", "2000"]
diff --git a/src/PersonalBrandAssistant.Web/nginx.conf b/src/PersonalBrandAssistant.Web/nginx.conf
new file mode 100644
index 0000000..30ed241
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/nginx.conf
@@ -0,0 +1,48 @@
+worker_processes auto;
+
+events {
+    worker_connections 1024;
+}
+
+http {
+    include /etc/nginx/mime.types;
+    default_type application/octet-stream;
+
+    sendfile on;
+    keepalive_timeout 65;
+
+    gzip on;
+    gzip_types text/html application/javascript text/css application/json image/svg+xml;
+    gzip_min_length 256;
+
+    server {
+        listen 80;
+        server_name _;
+        root /usr/share/nginx/html;
+        index index.html;
+
+        add_header X-Frame-Options "DENY" always;
+        add_header X-Content-Type-Options "nosniff" always;
+        add_header X-XSS-Protection "1; mode=block" always;
+        add_header Referrer-Policy "strict-origin-when-cross-origin" always;
+        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' http://api:8080;" always;
+
+        location /api/ {
+            proxy_pass http://api:8080/api/;
+            proxy_set_header Host $host;
+            proxy_set_header X-Real-IP $remote_addr;
+            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
+            proxy_set_header X-Forwarded-Proto $scheme;
+        }
+
+        location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
+            expires 1y;
+            add_header Cache-Control "public, immutable";
+        }
+
+        location / {
+            try_files $uri $uri/ /index.html;
+            add_header Cache-Control "no-cache";
+        }
+    }
+}
