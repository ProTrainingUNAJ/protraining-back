# Contenedor para cargar los datos de la demo (KAN-479).
# Usa la misma imagen que las bases (ya descargada) porque trae psql, y le suma bash, curl y jq.
# Así el seed corre igual en Windows, Mac y Linux: lo único que hace falta en la compu es Docker.
FROM postgres:16-alpine

RUN apk add --no-cache bash curl jq

# El script se monta desde ./scripts (ver el servicio "seed" del docker-compose.yml):
# los cambios en demo-data.sh se ven sin volver a construir la imagen.
ENTRYPOINT ["bash", "/scripts/demo-data.sh"]
CMD ["base"]
