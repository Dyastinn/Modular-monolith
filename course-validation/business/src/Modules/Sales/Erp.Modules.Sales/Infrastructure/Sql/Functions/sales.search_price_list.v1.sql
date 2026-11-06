-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.search_price_list(p_search text, p_for_sale_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               unit_price numeric, category_id bigint, category_name text, is_for_sale boolean, tax_type integer)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.sku::text, i.name::text, i.unit::text, i.allows_fractions, i.is_active,
           i.unit_price, i.category_id, c.name::text, i.is_for_sale, i.tax_type
    FROM sales.item i
    LEFT JOIN sales.item_category c ON c.id = i.category_id
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_for_sale_only OR (i.is_for_sale AND i.is_active))
    ORDER BY i.name, i.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION sales.count_price_list(p_search text, p_for_sale_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM sales.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_for_sale_only OR (i.is_for_sale AND i.is_active));
$$;

CREATE OR REPLACE FUNCTION sales.list_item_categories()
RETURNS TABLE (id bigint, name text, max_quantity_per_order numeric, required_role text)
LANGUAGE sql STABLE
AS $$
    SELECT c.id, c.name::text, c.max_quantity_per_order, c.required_role::text
    FROM sales.item_category c
    ORDER BY c.name;
$$;
