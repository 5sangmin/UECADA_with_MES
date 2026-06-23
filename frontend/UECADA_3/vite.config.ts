import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// 프론트 번들 사이즈를 줄이기 위해 무거운 vendor 라이브러리들을
// 별도 청크로 분리한다. 각 페이지가 자주 갱신되어도 vendor 청크는
// 캐시 히트하도록 만드는 효과도 있다.
export default defineConfig({
  plugins: [vue()],
  server: {
    // LAN 의 다른 PC / Unreal client / 태블릿에서 접근 가능하도록
    // 모든 인터페이스에 바인딩한다. host 만 잠그고 싶다면
    // VITE_DEV_HOST=127.0.0.1 같은 식으로 override 가능.
    host: process.env.VITE_DEV_HOST ?? '0.0.0.0',
    port: Number(process.env.VITE_DEV_PORT ?? 5173),
    strictPort: false,
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_API_PROXY ?? 'http://localhost:8080',
        changeOrigin: true,
      },
      '/health': {
        target: process.env.VITE_DEV_API_PROXY ?? 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  preview: {
    host: process.env.VITE_PREVIEW_HOST ?? '0.0.0.0',
    port: Number(process.env.VITE_PREVIEW_PORT ?? 4173),
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined
          if (id.includes('apexcharts') || id.includes('vue3-apexcharts')) {
            return 'vendor-apexcharts'
          }
          if (id.includes('echarts') || id.includes('vue-echarts') || id.includes('zrender')) {
            return 'vendor-echarts'
          }
          if (id.includes('lucide-vue-next')) {
            return 'vendor-lucide'
          }
          if (
            id.includes('@tanstack/vue-query') ||
            id.includes('@tanstack/query-core')
          ) {
            return 'vendor-vue-query'
          }
          if (id.includes('vue-router') || id.includes('pinia')) {
            return 'vendor-vue-libs'
          }
          if (id.includes('node_modules/vue/') || id.includes('@vue/')) {
            return 'vendor-vue'
          }
          if (id.includes('axios')) {
            return 'vendor-axios'
          }
          return 'vendor'
        },
      },
    },
  },
})
