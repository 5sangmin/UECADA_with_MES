package com.example.phm.sensor.opcua;

import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 설비 식별자 형식 변환 헬퍼.
 *
 * <p>시스템에 세 가지 식별자 형식이 공존한다:
 * <ul>
 *   <li>ns=3 OPC UA 토큰: {@code LINE01}, {@code CAST01} (대시 없음, 끝 2자리가 번호)</li>
 *   <li>DB {@code equipment_status.equip_id}: {@code LINE-01_CAST-01} (대시 + 언더스코어)</li>
 *   <li>sensor buffer key: {@code LINE01.CAST01:metric}</li>
 * </ul>
 *
 * <p>이 클래스는 ns=3 토큰 ↔ DB equip_id 양방향 변환을 담당한다.
 * 규약: 각 토큰의 마지막 2자리는 번호, 앞부분은 prefix 이며, 번호 앞에 대시를 삽입하고
 * line/eq 토큰을 언더스코어로 결합한다. (예: {@code LINE01}, {@code CAST01} → {@code LINE-01_CAST-01})
 */
public final class EquipmentIdConverter {

    // "LINE01" -> prefix=LINE, number=01 / "CAST01" -> prefix=CAST, number=01
    private static final Pattern TOKEN = Pattern.compile("^([A-Za-z]+)(\\d{2})$");
    // "LINE-01" / "CAST-01"
    private static final Pattern DASHED = Pattern.compile("^([A-Za-z]+)-(\\d{2})$");

    private EquipmentIdConverter() {
    }

    /** ns=3 토큰 2개({@code LINE01}, {@code CAST01}) → DB equip_id ({@code LINE-01_CAST-01}). */
    public static String toEquipId(String lineToken, String eqToken) {
        return dashify(lineToken) + "_" + dashify(eqToken);
    }

    /**
     * ns=3 node base({@code TOTAL_DAS.LINE01.CAST01}) → DB equip_id ({@code LINE-01_CAST-01}).
     * base 에 field 가 붙어 있어도({@code TOTAL_DAS.LINE01.CAST01.power}) line/eq 토큰만 사용한다.
     */
    public static String nodeBaseToEquipId(String base) {
        if (base == null || base.isBlank()) {
            return null;
        }
        String body = base.startsWith("TOTAL_DAS.")
                ? base.substring("TOTAL_DAS.".length())
                : base;
        String[] parts = body.split("\\.");
        if (parts.length < 2) {
            return null;
        }
        return toEquipId(parts[0], parts[1]);
    }

    /** DB equip_id ({@code LINE-01_CAST-01}) → ns=3 토큰 배열 {@code [LINE01, CAST01]}. */
    public static String[] toNs3Tokens(String equipId) {
        if (equipId == null) {
            return null;
        }
        int underscore = equipId.indexOf('_');
        if (underscore < 0) {
            return null;
        }
        return new String[] {
                undash(equipId.substring(0, underscore)),
                undash(equipId.substring(underscore + 1))
        };
    }

    private static String dashify(String token) {
        if (token == null) {
            throw new IllegalArgumentException("token is null");
        }
        Matcher matcher = TOKEN.matcher(token.trim().toUpperCase(Locale.ROOT));
        if (!matcher.matches()) {
            throw new IllegalArgumentException("Unexpected ns=3 token: " + token);
        }
        return matcher.group(1) + "-" + matcher.group(2);
    }

    private static String undash(String token) {
        Matcher matcher = DASHED.matcher(token.trim().toUpperCase(Locale.ROOT));
        if (!matcher.matches()) {
            return token.trim().replace("-", "").toUpperCase(Locale.ROOT);
        }
        return matcher.group(1) + matcher.group(2);
    }
}
