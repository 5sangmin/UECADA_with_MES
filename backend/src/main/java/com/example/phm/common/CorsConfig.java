package com.example.phm.common;

import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

/**
 * CORS 허용 도메인.
 * <p>
 * UECADA 프런트(localhost:5173) 외에, SMWP / KingPortal WebSCADA(222.108.180.36) 의
 * 페이지 스크립트(열기시 / 실행시)에서 fetch 로 /api/** 를 호출할 수 있도록 SMWP
 * 호스트도 허용 목록에 포함합니다. 새 환경을 추가할 때는 패턴만 늘리면 됩니다.
 * <p>
 * 사내 LAN PC(다른 직원 PC, 모바일 등)에서 frontend 로 접근하면 브라우저가
 * Origin 헤더를 그대로 보내기 때문에 RFC1918 사설망 대역도 허용 목록에 포함합니다.
 * (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16). vite proxy 가 changeOrigin 으로
 * forward host 만 바꿔주더라도 CORS preflight 의 Origin 검사는 브라우저 원본을
 * 기준으로 하기 때문에 이 허용이 필요합니다.
 */
@Configuration
public class CorsConfig implements WebMvcConfigurer {

    private static final String[] ALLOWED_ORIGIN_PATTERNS = {
            "http://localhost:*",
            "http://127.0.0.1:*",
            // SMWP / KingPortal WebSCADA — 열기시/실행시 스크립트에서 fetch 로 API 호출.
            "http://222.108.180.36:*",
            "https://222.108.180.36:*",
            "http://192.168.0.100:*",
            "https://192.168.0.100:*",
            // 사내 LAN (RFC1918 사설망 전 대역) — 외부 PC 가 frontend 에 접근할 때
            // 브라우저 Origin 이 LAN IP 로 찍히기 때문에 허용 필요.
            "http://192.168.*.*:*",
            "https://192.168.*.*:*",
            "http://10.*.*.*:*",
            "https://10.*.*.*:*",
            "http://172.16.*.*:*", "http://172.17.*.*:*", "http://172.18.*.*:*",
            "http://172.19.*.*:*", "http://172.20.*.*:*", "http://172.21.*.*:*",
            "http://172.22.*.*:*", "http://172.23.*.*:*", "http://172.24.*.*:*",
            "http://172.25.*.*:*", "http://172.26.*.*:*", "http://172.27.*.*:*",
            "http://172.28.*.*:*", "http://172.29.*.*:*", "http://172.30.*.*:*",
            "http://172.31.*.*:*",
            "https://172.16.*.*:*", "https://172.17.*.*:*", "https://172.18.*.*:*",
            "https://172.19.*.*:*", "https://172.20.*.*:*", "https://172.21.*.*:*",
            "https://172.22.*.*:*", "https://172.23.*.*:*", "https://172.24.*.*:*",
            "https://172.25.*.*:*", "https://172.26.*.*:*", "https://172.27.*.*:*",
            "https://172.28.*.*:*", "https://172.29.*.*:*", "https://172.30.*.*:*",
            "https://172.31.*.*:*"
    };

    @Override
    public void addCorsMappings(CorsRegistry registry) {
        registry.addMapping("/api/**")
                .allowedOriginPatterns(ALLOWED_ORIGIN_PATTERNS)
                .allowedMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .allowedHeaders("*")
                .allowCredentials(false)
                .maxAge(3600);
        registry.addMapping("/health")
                .allowedOriginPatterns(ALLOWED_ORIGIN_PATTERNS)
                .allowedMethods("GET", "OPTIONS")
                .allowedHeaders("*")
                .maxAge(3600);
    }
}
