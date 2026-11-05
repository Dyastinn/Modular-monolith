-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION purchasing.search_suppliers(p_supplier_id bigint, p_search text, p_active_only boolean,
                                                       p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, tin text, address text, email text, phone text, payment_terms_days integer, is_active boolean)
LANGUAGE sql STABLE
AS $$
    SELECT s.id, s.name::text, s.tin::text, s.address::text, s.email::text, s.phone::text, s.payment_terms_days, s.is_active
    FROM purchasing.supplier s
    WHERE (p_supplier_id IS NULL OR s.id = p_supplier_id)
      AND (p_search IS NULL OR s.name ILIKE '%' || p_search || '%' OR s.tin ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR s.is_active)
    ORDER BY s.name, s.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION purchasing.count_suppliers(p_search text, p_active_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM purchasing.supplier s
    WHERE (p_search IS NULL OR s.name ILIKE '%' || p_search || '%' OR s.tin ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR s.is_active);
$$;
