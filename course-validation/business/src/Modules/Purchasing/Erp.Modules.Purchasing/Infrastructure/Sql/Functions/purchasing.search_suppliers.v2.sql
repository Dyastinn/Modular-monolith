-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: sorting (NULL = by name). The count is unchanged (v1).
DROP FUNCTION IF EXISTS purchasing.search_suppliers(bigint, text, boolean, integer, integer);

CREATE OR REPLACE FUNCTION purchasing.search_suppliers(p_supplier_id bigint, p_search text, p_active_only boolean,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, tin text, address text, email text, phone text, payment_terms_days integer, is_active boolean)
LANGUAGE sql STABLE
AS $$
    WITH s AS (
        SELECT s.id, s.name::text AS name, s.tin::text AS tin, s.address::text AS address, s.email::text AS email,
               s.phone::text AS phone, s.payment_terms_days, s.is_active
        FROM purchasing.supplier s
        WHERE (p_supplier_id IS NULL OR s.id = p_supplier_id)
          AND (p_search IS NULL OR s.name ILIKE '%' || p_search || '%' OR s.tin ILIKE '%' || p_search || '%')
          AND (NOT p_active_only OR s.is_active)
    )
    SELECT * FROM s
    ORDER BY
        CASE WHEN p_sort = 'paymentTermsDays' AND NOT p_desc THEN s.payment_terms_days END ASC,
        CASE WHEN p_sort = 'paymentTermsDays' AND p_desc     THEN s.payment_terms_days END DESC,
        CASE WHEN p_sort = 'name' AND p_desc THEN s.name END DESC,
        s.name, s.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
