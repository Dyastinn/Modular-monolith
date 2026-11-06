-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.list_special_discount_programs(p_include_inactive boolean)
RETURNS TABLE (code text, name text, id_label text, rate numeric, vat_exempt boolean, is_active boolean)
LANGUAGE sql STABLE
AS $$
    SELECT p.code::text, p.name::text, p.id_label::text, p.rate, p.vat_exempt, p.is_active
    FROM sales.special_discount_program p
    WHERE p_include_inactive OR p.is_active
    ORDER BY p.name;
$$;
