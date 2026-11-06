-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION invoicing.get_company_profile()
RETURNS TABLE (registered_name text, trade_name text, tin text, registered_address text, tax_regime integer, vat_rate numeric,
               invoice_title text, permit_number text, series_range text, permit_date_issued text, updated_by bigint,
               updated_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT p.registered_name::text, p.trade_name::text, p.tin::text, p.registered_address::text, p.tax_regime, p.vat_rate,
           p.invoice_title::text, p.permit_number::text, p.series_range::text, p.permit_date_issued::text, p.updated_by,
           p.updated_utc
    FROM invoicing.company_profile p
    WHERE p.id = 1;
$$;
