-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: sorting (p_sort is one of the validator's names; NULL = by name). The count and categories are unchanged (v1).
DROP FUNCTION IF EXISTS sales.search_price_list(text, boolean, integer, integer);

CREATE OR REPLACE FUNCTION sales.search_price_list(p_search text, p_for_sale_only boolean, p_sort text, p_desc boolean,
    p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               unit_price numeric, category_id bigint, category_name text, is_for_sale boolean, tax_type integer,
               is_special_discount_eligible boolean)
LANGUAGE sql STABLE
AS $$
    WITH i AS (
        SELECT i.id, i.sku::text AS sku, i.name::text AS name, i.unit::text AS unit, i.allows_fractions, i.is_active,
               i.unit_price, i.category_id, c.name::text AS category_name, i.is_for_sale, i.tax_type, i.is_special_discount_eligible
        FROM sales.item i
        LEFT JOIN sales.item_category c ON c.id = i.category_id
        WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
          AND (NOT p_for_sale_only OR (i.is_for_sale AND i.is_active))
    )
    SELECT * FROM i
    ORDER BY
        CASE WHEN p_sort = 'sku' AND NOT p_desc THEN i.sku END ASC,
        CASE WHEN p_sort = 'sku' AND p_desc     THEN i.sku END DESC,
        CASE WHEN p_sort = 'unitPrice' AND NOT p_desc THEN i.unit_price END ASC,
        CASE WHEN p_sort = 'unitPrice' AND p_desc     THEN i.unit_price END DESC,
        CASE WHEN p_sort = 'categoryName' AND NOT p_desc THEN i.category_name END ASC,
        CASE WHEN p_sort = 'categoryName' AND p_desc     THEN i.category_name END DESC,
        CASE WHEN p_sort = 'name' AND p_desc THEN i.name END DESC,
        i.name, i.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
