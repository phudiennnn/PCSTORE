FROM node:22-alpine AS build
WORKDIR /app
COPY PCStore.Web/package.json PCStore.Web/package-lock.json ./
RUN npm ci
COPY PCStore.Web/ ./
RUN npm run build

FROM nginx:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
