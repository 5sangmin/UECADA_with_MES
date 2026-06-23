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
    // vite 5.4.12+ 부터 CVE-2025-30208 패치로 외부 Host 헤더 검증이 강화되어
    // LAN IP(예: 192.168.5.10:5173)로 들어오는 요청이 기본적으로 403 으로
    // 차단될 수 있다. dev/사내 LAN 용도라 모든 host 를 허용한다.
    // 운영 환경에서는 구체적 host 목록을 주입하는 것을 권장.
    allowedHosts: true,
    // dev server 자체 CORS 를 열어 외부 origin 의 정적 리소스 fetch 도 허용
    cors: true,
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
    allowedHosts: true,
    cors: true,
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
