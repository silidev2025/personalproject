FROM php:8.3-apache

# pdo_sqlite is what the panel stores everything in
RUN docker-php-ext-install pdo_sqlite \
    && a2enmod rewrite

# .htaccess is ignored unless AllowOverride is on
RUN sed -ri 's/AllowOverride\s+None/AllowOverride All/g' /etc/apache2/apache2.conf

COPY . /var/www/html/

# the database directory has to survive a redeploy, so it lives on a volume
RUN mkdir -p /var/www/html/data \
    && chown -R www-data:www-data /var/www/html/data

VOLUME ["/var/www/html/data"]

EXPOSE 80
